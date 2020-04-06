﻿using ColossalFramework;
using Klyte.Commons;
using Klyte.Commons.Interfaces;
using Klyte.Commons.Utils;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Klyte.TreesRespiration
{
    public class TRController : BaseController<TreesRespirationMod, TRController>
    {
        public static readonly string FOLDER_NAME = "TreesRespiration";
        public static readonly string FOLDER_PATH = FileUtils.BASE_FOLDER_PATH + FOLDER_NAME;

        public const int MAX_ACCURACY_VALUE = 12;

        private static readonly int[] m_strenghts = new int[] { 0, 1, 2, 4, 8, 16, 32, 64, 128 };
        internal static SavedInt MultiplierTrees { get; } = new SavedInt($"K45_{ CommonProperties.Acronym }_MultiplierTrees", Settings.gameSettingsFile, 0, true);
        internal static SavedInt MultiplierBuildings { get; } = new SavedInt($"K45_{ CommonProperties.Acronym }_MultiplierBuildings", Settings.gameSettingsFile, 0, true);
        internal static SavedInt MultiplierNet { get; } = new SavedInt($"K45_{ CommonProperties.Acronym }_MultiplierNet", Settings.gameSettingsFile, 0, true);
        internal static SavedInt SimulationAccuracy { get; } = new SavedInt($"K45_{ CommonProperties.Acronym }_SimulationAccuracy", Settings.gameSettingsFile, 5, true);

        private static readonly Dictionary<string, Tuple<int, float>> m_cachedValues = new Dictionary<string, Tuple<int, float>>();

        private uint m_treesDivisor;
        private uint m_buildingsDivisor;
        private uint m_netsDivisor;

        private uint m_strengthOffset;

        public void Awake() => UpdateDivisors();

        internal void UpdateDivisors()
        {
            uint divisor = 1u << (Math.Max(1, Math.Min(MAX_ACCURACY_VALUE, SimulationAccuracy)) + 3);
            m_treesDivisor = FixedMath.GEqualPowerOf2(TreeManager.instance.m_trees.m_size / divisor);
            m_buildingsDivisor = FixedMath.GEqualPowerOf2(BuildingManager.instance.m_buildings.m_size / divisor);
            m_netsDivisor = FixedMath.GEqualPowerOf2(NetManager.instance.m_segments.m_size / divisor);
            m_strengthOffset = 1u << (MAX_ACCURACY_VALUE - SimulationAccuracy);
            LogUtils.DoLog($"Items processed per frame = {divisor}");
        }

        public const TreeInstance.Flags SEARCH_FLAGS_TREE = TreeInstance.Flags.Created | TreeInstance.Flags.Hidden;
        public const TreeInstance.Flags MATCH_FLAGS_TREE = TreeInstance.Flags.Created;
        public const Building.Flags SEARCH_FLAGS_BUILDING = Building.Flags.Created | Building.Flags.Abandoned | Building.Flags.Collapsed | Building.Flags.Completed | Building.Flags.Hidden | Building.Flags.Flooded | Building.Flags.BurnedDown;
        public const Building.Flags MATCH_FLAGS_BUILDING = Building.Flags.Created | Building.Flags.Completed;
        public const NetSegment.Flags SEARCH_FLAGS_SEGMENT = NetSegment.Flags.Created | NetSegment.Flags.Flooded;
        public const NetSegment.Flags MATCH_FLAGS_SEGMENT = NetSegment.Flags.Created;

        public void Update()
        {
            #region Trees

            uint frameIdx = SimulationManager.instance.m_currentTickIndex;
            if (TRController.MultiplierTrees.value > 0)
            {
                TreeManager __instance = TreeManager.instance;
                for (uint i = frameIdx % m_treesDivisor; i < __instance.m_trees.m_buffer.Length; i += m_treesDivisor)
                {
                    if (((TreeInstance.Flags)__instance.m_trees.m_buffer[i].m_flags & SEARCH_FLAGS_TREE) == MATCH_FLAGS_TREE)
                    {
                        DepolluteTree(__instance.m_trees.m_buffer[i].Info, __instance.m_trees.m_buffer[i].Position, TRController.MultiplierTrees.value);
                    }
                }
            }
            #endregion
            #region Buildings
            if (TRController.MultiplierBuildings.value > 0)
            {
                BuildingManager __instance = BuildingManager.instance;
                for (uint i = frameIdx % m_buildingsDivisor; i < __instance.m_buildings.m_buffer.Length; i += m_buildingsDivisor)
                {
                    if ((__instance.m_buildings.m_buffer[i].m_flags & SEARCH_FLAGS_BUILDING) == MATCH_FLAGS_BUILDING)
                    {
                        ProcessBuilding(ref __instance.m_buildings.m_buffer[i]);
                    }
                }
            }
            #endregion
            #region Nets
            if (TRController.MultiplierNet.value > 0)
            {
                NetManager __instance = NetManager.instance;
                for (uint i = frameIdx % m_netsDivisor; i < __instance.m_segments.m_buffer.Length; i += m_netsDivisor)
                {
                    if ((__instance.m_segments.m_buffer[i].m_flags & SEARCH_FLAGS_SEGMENT) == MATCH_FLAGS_SEGMENT)
                    {
                        ProcessSegment(ref __instance.m_segments.m_buffer[i]);
                    }
                }
            }
            #endregion
        }


        private void DepolluteTree(TreeInfo info, Vector3 position, int multiplier)
        {
            if (!m_cachedValues.TryGetValue(info.name, out Tuple<int, float> dimensions))
            {
                Bounds b = (info.m_mesh ?? info.m_lodMesh16)?.bounds ?? new Bounds();
                int valuePollution = -Math.Max((int)b.size.magnitude, 10);
                float valueRadius = Math.Max(b.size.y, 2);
                LogUtils.DoLog($"Add Val for {info.name} (val = {valuePollution} | dist = {valueRadius})");
                dimensions = Tuple.New(valuePollution, valueRadius);
                m_cachedValues[info.name] = dimensions;
            }
            Singleton<NaturalResourceManager>.instance.TryDumpResource(NaturalResourceManager.Resource.Pollution, (int)(dimensions.First * multiplier * m_strengthOffset / 8), 0, position, dimensions.Second);
        }

        public void ProcessBuilding(ref Building data)
        {
            if ((data.Info?.m_props?.Length ?? 0) == 0)
            {
                return;
            }

            for (int i = 0; i < data.Info.m_props.Length; i++)
            {
                if (data.Info.m_props[i]?.m_tree != null)
                {
                    DepolluteTree(data.Info.m_props[i].m_tree, data.CalculatePosition(data.Info.m_props[i].m_position), TRController.MultiplierBuildings.value);
                }
            }
        }
        public void ProcessSegment(ref NetSegment data)
        {
            if (data.Info?.m_lanes == null)
            {
                return;
            }

            for (int l = 0; l < data.Info.m_lanes.Length; l++)
            {
                if (data.Info.m_lanes[l]?.m_laneProps == null)
                {
                    continue;
                }

                for (int j = 0; j < data.Info.m_lanes[l].m_laneProps.m_props.Length; j++)
                {
                    if (data.Info.m_lanes[l]?.m_laneProps.m_props[j].m_tree != null)
                    {
                        DepolluteTree(data.Info.m_lanes[l]?.m_laneProps.m_props[j].m_tree, data.m_middlePosition, TRController.MultiplierNet.value);
                        DepolluteTree(data.Info.m_lanes[l]?.m_laneProps.m_props[j].m_tree, NetManager.instance.m_nodes.m_buffer[data.m_startNode].m_position, TRController.MultiplierNet.value);
                        DepolluteTree(data.Info.m_lanes[l]?.m_laneProps.m_props[j].m_tree, NetManager.instance.m_nodes.m_buffer[data.m_endNode].m_position, TRController.MultiplierNet.value);
                    }
                }
            }
        }

    }
}