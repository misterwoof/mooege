﻿/*
 * Copyright (C) 2011 mooege project
 *
 * This program is free software; you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation; either version 2 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA  02111-1307  USA
 */

using System.Collections.Generic;
using System.Linq;
using Mooege.Common.Helpers.Math;
using Mooege.Common.Logging;
using Mooege.Common.MPQ;
using Mooege.Core.GS.Common.Types.Math;
using Mooege.Core.GS.Common.Types.SNO;
using Mooege.Core.GS.Games;
using Mooege.Core.GS.Map;
using Mooege.Core.GS.Common.Types.TagMap;
using Mooege.Common.MPQ.FileFormats;
using World = Mooege.Core.GS.Map.World;
using Scene = Mooege.Core.GS.Map.Scene;
using Mooege.Core.GS.Common.Types.Scene;



namespace Mooege.Core.GS.Generators
{
    public static class WorldGenerator
    {
        static readonly Logger Logger = LogManager.CreateLogger();

        

        public static World Generate(Game game, int worldSNO)
        {
            if (!MPQStorage.Data.Assets[SNOGroup.Worlds].ContainsKey(worldSNO))
            {
                Logger.Error("Can't find a valid world definition for sno: {0}", worldSNO);
                return null;
            }

            var worldAsset = MPQStorage.Data.Assets[SNOGroup.Worlds][worldSNO];
            var worldData = (Mooege.Common.MPQ.FileFormats.World)worldAsset.Data;


            if (worldData.IsGenerated)
            {
                Logger.Error("World {0} [{1}] is a dynamic world! Can't generate proper dynamic worlds yet!", worldAsset.Name, worldAsset.SNOId);

                return GenerateRandomDungeon(game, worldSNO, worldData);
                //return null;
            }

            var world = new World(game, worldSNO);
            var levelAreas = new Dictionary<int, List<Scene>>();

            // Create a clusterID => Cluster Dictionary
            var clusters = new Dictionary<int, Mooege.Common.MPQ.FileFormats.SceneCluster>();
            foreach (var cluster in worldData.SceneClusterSet.SceneClusters)
                clusters[cluster.ClusterId] = cluster;

            // Scenes are not aligned to (0, 0) but apparently need to be -farmy
            float minX = worldData.SceneParams.SceneChunks.Min(x => x.PRTransform.Vector3D.X);
            float minY = worldData.SceneParams.SceneChunks.Min(x => x.PRTransform.Vector3D.Y);

            // Count all occurences of each cluster /fasbat
            var clusterCount = new Dictionary<int, int>();

            foreach (var sceneChunk in worldData.SceneParams.SceneChunks)
            {
                var cID = sceneChunk.SceneSpecification.ClusterID;
                if (cID != -1 && clusters.ContainsKey(cID)) // Check for wrong clusters /fasbat
                {
                    if (!clusterCount.ContainsKey(cID))
                        clusterCount[cID] = 0;
                    clusterCount[cID]++;
                }
            }

            // For each cluster generate a list of randomly selected subcenes /fasbat
            var clusterSelected = new Dictionary<int, List<Mooege.Common.MPQ.FileFormats.SubSceneEntry>>();
            foreach (var cID in clusterCount.Keys)
            {
                var selected = new List<Mooege.Common.MPQ.FileFormats.SubSceneEntry>();
                clusterSelected[cID] = selected;
                var count = clusterCount[cID];
                foreach (var group in clusters[cID].SubSceneGroups) // First select from each subscene group /fasbat
                {
                    for (int i = 0; i < group.I0 && count > 0; i++, count--) //TODO Rename I0 to requiredCount? /fasbat
                    {
                        var subSceneEntry = RandomHelper.RandomItem(group.Entries, entry => entry.Probability);
                        selected.Add(subSceneEntry);
                    }

                    if (count == 0)
                        break;
                }

                while (count > 0) // Fill the rest with defaults /fasbat
                {
                    var subSceneEntry = RandomHelper.RandomItem(clusters[cID].Default.Entries, entry => entry.Probability);
                    selected.Add(subSceneEntry);
                    count--;
                }
            }

            foreach (var sceneChunk in worldData.SceneParams.SceneChunks)
            {
                var position = sceneChunk.PRTransform.Vector3D - new Vector3D(minX, minY, 0);
                var scene = new Scene(world, position, sceneChunk.SNOHandle.Id, null)
                {
                    MiniMapVisibility = true,
                    RotationW = sceneChunk.PRTransform.Quaternion.W,
                    RotationAxis = sceneChunk.PRTransform.Quaternion.Vector3D,
                    SceneGroupSNO = -1
                };
               
                // If the scene has a subscene (cluster ID is set), choose a random subscenes from the cluster load it and attach it to parent scene /farmy
                if (sceneChunk.SceneSpecification.ClusterID != -1)
                {
                    if (!clusters.ContainsKey(sceneChunk.SceneSpecification.ClusterID))
                    {
                        Logger.Warn("Referenced clusterID {0} not found for chunk {1} in world {2}", sceneChunk.SceneSpecification.ClusterID, sceneChunk.SNOHandle.Id, worldSNO);
                    }
                    else
                    {
                        var entries = clusterSelected[sceneChunk.SceneSpecification.ClusterID]; // Select from our generated list /fasbat
                        Mooege.Common.MPQ.FileFormats.SubSceneEntry subSceneEntry = null;

                        if (entries.Count > 0)
                        {
                            //subSceneEntry = entries[RandomHelper.Next(entries.Count - 1)];

                            subSceneEntry = RandomHelper.RandomItem<Mooege.Common.MPQ.FileFormats.SubSceneEntry>(entries, entry => 1); // TODO Just shuffle the list, dont random every time. /fasbat
                            entries.Remove(subSceneEntry);
                        }
                        else
                            Logger.Error("No SubScenes defined for cluster {0} in world {1}", sceneChunk.SceneSpecification.ClusterID, world.DynamicID);

                        Vector3D pos = FindSubScenePosition(sceneChunk); // TODO According to BoyC, scenes can have more than one subscene, so better enumerate over all subscenepositions /farmy

                        if (pos == null)
                        {
                            Logger.Error("No scene position marker for SubScenes of Scene {0} found", sceneChunk.SNOHandle.Id);
                        }
                        else
                        {
                            var subScenePosition = scene.Position + pos;
                            var subscene = new Scene(world, subScenePosition, subSceneEntry.SNOScene, scene)
                            {
                                MiniMapVisibility = true,
                                RotationW = sceneChunk.PRTransform.Quaternion.W,
                                RotationAxis = sceneChunk.PRTransform.Quaternion.Vector3D,
                                Specification = sceneChunk.SceneSpecification
                            };
                            scene.Subscenes.Add(subscene);
                            subscene.LoadMarkers();
                        }
                    }

                }
                scene.Specification = sceneChunk.SceneSpecification;
                scene.LoadMarkers();

                // add scene to level area dictionary
                foreach (var levelArea in scene.Specification.SNOLevelAreas)
                {
                    if (levelArea != -1)
                    {
                        if (!levelAreas.ContainsKey(levelArea))
                            levelAreas.Add(levelArea, new List<Scene>());

                        levelAreas[levelArea].Add(scene);
                    }
                }
            }

            loadLevelAreas(levelAreas, world);
            return world;
        }

        private static World GenerateRandomDungeon(Game game, int worldSNO, Mooege.Common.MPQ.FileFormats.World worldData)
        {
            var world = new World(game, worldSNO);

            Dictionary<int, TileInfo> tiles = new Dictionary<int, TileInfo>();

            foreach (var drlgparam in worldData.DRLGParams)
            {
                foreach (var tile in drlgparam.Tiles)
                {
                    tiles.Add(tile.SNOScene, tile);
                }
            }

            var tilesByType = new Dictionary<Mooege.Common.MPQ.FileFormats.TileTypes, List<Mooege.Common.MPQ.FileFormats.TileInfo>>();
            //HACK For defiler's crypt.
            //TODO: FIX THIS.
            foreach (var tile in tiles.Values)
            {
                Logger.Debug("RandomGeneration: TileType: {0}", (TileTypes)tile.TileType);
                if (!tilesByType.ContainsKey((TileTypes)tile.TileType))
                    tilesByType[(TileTypes)tile.TileType] = new List<Mooege.Common.MPQ.FileFormats.TileInfo>();
                tilesByType[(TileTypes)tile.TileType].Add(tile);
            }            
                var entrance = RandomHelper.RandomItem(tilesByType[Mooege.Common.MPQ.FileFormats.TileTypes.Entrance], entry => 1);
                AddTile(world, entrance, new Vector3D(0, 0, 0));
                var filler = RandomHelper.RandomItem(tilesByType[Mooege.Common.MPQ.FileFormats.TileTypes.Normal], entry => 1);
                Logger.Debug("RandomGeneration: Normal SNO TILE (32960): {0}", filler.SNOScene);
                
                AddTile(world, tiles[32960], new Vector3D(240,0,0));
                var exit = RandomHelper.RandomItem(tilesByType[Mooege.Common.MPQ.FileFormats.TileTypes.Exit], entry => 1);

                Logger.Debug("RandomGeneration: Exit SNO TILE: (not 174633, 174643) {0}", exit.SNOScene);
                AddTile(world, tiles[174663], new Vector3D(240, 240, 0));
            return world;
        }


        private static void AddTile(World world, TileInfo tileInfo, Vector3D location)
        {
            var levelAreas = new Dictionary<int, List<Scene>>();
            var scene = new Scene(world, location, tileInfo.SNOScene, null);
            scene.MiniMapVisibility = true; // SceneMiniMapVisibility.Visited;
            //scene.Position = new Vector3D(0, 0, 0);
            scene.RotationW = 1.0f; //scene.RotationAmount = 1.0f;
            scene.RotationAxis = new Vector3D(0, 0, 0);
            scene.SceneGroupSNO = -1;

            var spec = new SceneSpecification();
            scene.Specification = spec;
            spec.Cell = new Vector2D() { X = 0, Y = 0 };
            spec.CellZ = 0;
            spec.SNOLevelAreas = new int[] { 154588, -1, -1, -1 };
            spec.SNOMusic = -1;
            spec.SNONextLevelArea = -1;
            spec.SNONextWorld = -1;
            spec.SNOPresetWorld = -1;
            spec.SNOPrevLevelArea = -1;
            spec.SNOPrevWorld = -1;
            spec.SNOReverb = -1;
            spec.SNOWeather = 50542;
            spec.SNOCombatMusic = -1;
            spec.SNOAmbient = -1;
            spec.ClusterID = -1;
            spec.Unknown1 = 14;
            spec.Unknown3 = 5;
            spec.Unknown4 = -1;
            spec.Unknown5 = 0;
            spec.SceneCachedValues = new SceneCachedValues();
            spec.SceneCachedValues.Unknown1 = 63;
            spec.SceneCachedValues.Unknown2 = 96;
            spec.SceneCachedValues.Unknown3 = 96;
            var sceneFile = MPQStorage.Data.Assets[SNOGroup.Scene][tileInfo.SNOScene];
            var sceneData = (Mooege.Common.MPQ.FileFormats.Scene)sceneFile.Data;
            spec.SceneCachedValues.AABB1 = sceneData.AABBBounds;
            spec.SceneCachedValues.AABB2 = sceneData.AABBMarketSetBounds;
            spec.SceneCachedValues.Unknown4 = new int[4] { 0, 0, 0, 0 };

            scene.LoadMarkers();

            // add scene to level area dictionary
            foreach (var levelArea in scene.Specification.SNOLevelAreas)
            {
                if (levelArea != -1)
                {
                    if (!levelAreas.ContainsKey(levelArea))
                        levelAreas.Add(levelArea, new List<Scene>());

                    levelAreas[levelArea].Add(scene);
                }
            }

            loadLevelAreas(levelAreas, world);
        }

        /// <summary>
        /// Loads content for level areas. Call this after scenes have been generated and after scenes have their GizmoLocations
        /// set (this is done in Scene.LoadActors right now)
        /// </summary>
        /// <param name="levelAreas">Dictionary that for every level area has the scenes it consists of</param>
        /// <param name="world">The world to which to add loaded actors</param>
        private static void loadLevelAreas(Dictionary<int, List<Scene>> levelAreas, World world)
        {
            /// Each Scene has one to four level areas assigned to it. I dont know if that means
            /// the scene belongs to both level areas or if the scene is split
            /// Scenes marker tags have generic GizmoLocationA to Z that are used 
            /// to provide random spawning possibilities.
            /// For each of these 26 LocationGroups, the LevelArea has a entry in its SpawnType array that defines
            /// what type of actor/encounter/adventure could spawn there
            /// 
            /// It could for example define, that for a level area X, out of the four spawning options
            /// two are randomly picked and have barrels placed there

            // Create an array of mobs, used with the loadActor in the load monster area loop
            // Each monster are created in Mooege.Core.GS.Actors.Implementations.Monsters
            // By Poluxxx
            int[] aSNO = new int[] { 
                      6652      // Zombie
                    , 6646      // Ravenous
                    , 370       // Ghost_A - Enraged Phantom 
                    , 4982      // QuillDemon
            };

            foreach (int la in levelAreas.Keys)
            {
                SNOHandle levelAreaHandle = new SNOHandle(SNOGroup.LevelArea, la);
                if (!levelAreaHandle.IsValid)
                {
                    Logger.Warn("Level area {0} does not exist", la);
                    continue;
                }
                var levelArea = levelAreaHandle.Target as LevelArea;

                for (int i = 0; i < 26; i++)
                {
                    // Merge the gizmo starting locations from all scenes and
                    // their subscenes into a single list for the whole level area
                    List<PRTransform> gizmoLocations = new List<PRTransform>();
                    foreach (var scene in levelAreas[la])
                    {
                        if (scene.GizmoSpawningLocations[i] != null)
                            gizmoLocations.AddRange(scene.GizmoSpawningLocations[i]);
                        foreach (Scene subScene in scene.Subscenes)
                        {
                            if (subScene.GizmoSpawningLocations[i] != null)
                                gizmoLocations.AddRange(subScene.GizmoSpawningLocations[i]);
                        }
                    }

                    // Load all spawns that are defined for that location group 
                    foreach (GizmoLocSpawnEntry spawnEntry in levelArea.LocSet.SpawnType[i].SpawnEntry)
                    {
                        // Get a random amount of spawns ...
                        int amount = RandomHelper.Next(spawnEntry.Max, spawnEntry.Max);
                        if (amount > gizmoLocations.Count)
                        {
                            Logger.Warn("Breaking after spawnEntry {0} for LevelArea {1} because there are less locations ({2}) than spawn amount ({3}, {4} min)", spawnEntry.SNOHandle, levelAreaHandle, gizmoLocations.Count, amount, spawnEntry.Min);
                            break;
                        }

                        Logger.Trace("Spawning {0} ({3} - {4} {1} in {2}", amount, spawnEntry.SNOHandle, levelAreaHandle, spawnEntry.Min, spawnEntry.Max);

                        // ...and place each one on a random position within the location group
                        for (; amount > 0; amount--)
                        {
                            int location = RandomHelper.Next(gizmoLocations.Count - 1);

                            switch (spawnEntry.SNOHandle.Group)
                            {
                                case SNOGroup.Actor:

                                    loadActor(spawnEntry.SNOHandle, gizmoLocations[location], world, new TagMap());
                                    break;

                                case SNOGroup.Encounter:

                                    var encounter = spawnEntry.SNOHandle.Target as Encounter;
                                    var actor = RandomHelper.RandomItem(encounter.Spawnoptions, x => x.Probability);
                                    loadActor(new SNOHandle(actor.SNOSpawn), gizmoLocations[location], world, new TagMap());
                                    break;

                                case SNOGroup.Adventure:

                                    // Adventure are basically made up of a markerSet that has relative PRTransforms
                                    // it has some other fields that are always 0 and a reference to a symbol actor
                                    // no idea what they are used for - farmy
                                    
                                    var adventure = spawnEntry.SNOHandle.Target as Adventure;
                                    var markerSet = new SNOHandle(adventure.SNOMarkerSet).Target as MarkerSet;

                                    foreach (var marker in markerSet.Markers)
                                    {
                                        // relative marker set coordinates to absolute world coordinates
                                        var absolutePRTransform = new PRTransform
                                        {
                                            Vector3D = marker.PRTransform.Vector3D + gizmoLocations[location].Vector3D,
                                            Quaternion = new Quaternion
                                            {
                                                Vector3D = new Vector3D(marker.PRTransform.Quaternion.Vector3D.X, marker.PRTransform.Quaternion.Vector3D.Y, marker.PRTransform.Quaternion.Vector3D.Z),
                                                W = marker.PRTransform.Quaternion.W
                                            }
                                        };

                                        switch (marker.Type)
                                        {
                                            case MarkerType.Actor:

                                                loadActor(marker.SNOHandle, absolutePRTransform, world, marker.TagMap);
                                                break;

                                            case MarkerType.Encounter:

                                                var encounter2 = marker.SNOHandle.Target as Encounter;
                                                var actor2 = RandomHelper.RandomItem(encounter2.Spawnoptions, x => x.Probability);
                                                loadActor(new SNOHandle(actor2.SNOSpawn), absolutePRTransform, world, marker.TagMap);
                                                break;

                                            default:

                                                Logger.Warn("Unhandled marker type {0} in actor loading", marker.Type);
                                                break;
                                        }
                                    }

                                    break;

                                default:

                                    if (spawnEntry.SNOHandle.Id != -1)
                                        Logger.Warn("Unknown sno handle in LevelArea spawn entries: {0}", spawnEntry.SNOHandle);
                                    break;

                            }

                            // dont use that location again
                            gizmoLocations.RemoveAt(location);

                        }
                    }
                }



                // Load monsters for level area
                foreach (var scene in levelAreas[la])
                {
                    // HACK: don't spawn monsters in tristram town scenes /mdz
                    if (MPQStorage.Data.Assets[SNOGroup.Scene][scene.SceneSNO.Id].Name.StartsWith("trOut_Tristram_"))
                        continue;
                    

                    for (int i = 0; i < 100; i++)
                    {
                        if (RandomHelper.NextDouble() > 0.8)
                        {
                            // TODO Load correct spawn population
                             // 2.5 is units per square, TODO: Find out how to calculate units per square. Is it F1 * V0.I1 / SquareCount?
                            int x = RandomHelper.Next(scene.NavMesh.SquaresCountX);
                            int y = RandomHelper.Next(scene.NavMesh.SquaresCountY);

                            if ((scene.NavMesh.Squares[y * scene.NavMesh.SquaresCountX + x].Flags & Mooege.Common.MPQ.FileFormats.Scene.NavCellFlags.NoSpawn) == 0)
                            {
                                loadActor(
                                    new SNOHandle(aSNO[RandomHelper.Next(aSNO.Length)]), // Poluxxx
                                    new PRTransform
                                    {
                                        Vector3D = new Vector3D
                                        {
                                            X = (float)(x * 2.5 + scene.Position.X),
                                            Y = (float)(y * 2.5 + scene.Position.Y),
                                            Z = scene.NavMesh.Squares[y * scene.NavMesh.SquaresCountX + x].Z + scene.Position.Z
                                        },
                                        Quaternion = new Quaternion
                                        {
                                            W = (float)(RandomHelper.NextDouble() * System.Math.PI * 2),
                                            Vector3D = new Vector3D(0, 0, 1)
                                        }
                                    },
                                    world,
                                    new TagMap()
                                    );
                            }
                        }
                    }
                }



            }
        }


        private static void loadActor(SNOHandle actorHandle, PRTransform location, World world, TagMap tagMap)
        {
            var actor = Mooege.Core.GS.Actors.ActorFactory.Create(world, actorHandle.Id, tagMap);

            if (actor == null)
            {
                if(actorHandle.Id != -1)
                    Logger.Warn("ActorFactory did not load actor {0}", actorHandle);
                return;
            }

            actor.RotationW = location.Quaternion.W;
            actor.RotationAxis = location.Quaternion.Vector3D;
            actor.EnterWorld(location.Vector3D);
        }



        /// <summary>
        /// Loads all markersets of a scene and looks for the one with the subscene position
        /// </summary>
        private static Vector3D FindSubScenePosition(Mooege.Common.MPQ.FileFormats.SceneChunk sceneChunk)
        {
            var mpqScene = MPQStorage.Data.Assets[SNOGroup.Scene][sceneChunk.SNOHandle.Id].Data as Mooege.Common.MPQ.FileFormats.Scene;

            foreach (var markerSet in mpqScene.MarkerSets)
            {
                var mpqMarkerSet = MPQStorage.Data.Assets[SNOGroup.MarkerSet][markerSet].Data as Mooege.Common.MPQ.FileFormats.MarkerSet;
                foreach (var marker in mpqMarkerSet.Markers)
                    if (marker.Type == Mooege.Common.MPQ.FileFormats.MarkerType.SubScenePosition)
                        return marker.PRTransform.Vector3D;
            }

            return null;
        }
    }
}