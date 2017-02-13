﻿using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace BetterPathfinding
{
	public class RegionLinkDijkstra
	{

		private struct RegionLinkQueueEntry
		{
			public readonly Region FromRegion;
			public readonly RegionLink Link;
			public readonly int Cost;
            public readonly int EstimatedPathCost;

			public RegionLinkQueueEntry(Region from, RegionLink l, int c, int tc)
			{
				FromRegion = from;
				Link = l;
				Cost = c;
                EstimatedPathCost = tc;
			}
		}


		private class DistanceComparer : IComparer<RegionLinkQueueEntry>
		{
			public int Compare(RegionLinkQueueEntry a, RegionLinkQueueEntry b)
			{
				return a.EstimatedPathCost.CompareTo(b.EstimatedPathCost);
			}
		}
        
		private readonly Dictionary<int, RegionLink> regionMinLink = new Dictionary<int, RegionLink>();

		private readonly Dictionary<RegionLink, int> distances = new Dictionary<RegionLink, int>();

	    private readonly Dictionary<RegionLink, IntVec3> linkTargetCells = new Dictionary<RegionLink, IntVec3>();

		private readonly FastPriorityQueue<RegionLinkQueueEntry> queue = new FastPriorityQueue<RegionLinkQueueEntry>(new DistanceComparer());
		
        private TraverseParms traverseParms;

        private readonly IntVec3 targetCell;

		private readonly ByteGrid avoidGrid;

		private readonly Area area;

		private readonly Map map;

	    private readonly NewPathFinder.PawnPathCostSettings pathCostSettings;

        public static int nodes_popped;

		private readonly Dictionary<Region, int> minPathCosts = new Dictionary<Region, int>();

		//private NewPathFinder debugPathfinder;
		public IntVec3 rootCell;

		public RegionLinkDijkstra(Map map, IntVec3 rootCell, IEnumerable<Region> startingRegions, IntVec3 target, TraverseParms parms, NewPathFinder.PawnPathCostSettings pathCosts)
		{
			this.map = map;
            this.traverseParms = parms;
            this.targetCell = target;
			this.rootCell = rootCell;
		    this.pathCostSettings = pathCosts;
			avoidGrid = pathCosts.avoidGrid;
			area = pathCosts.area;
			nodes_popped = 0;

			//if (DebugViewSettings.drawPaths && !NewPathFinder.disableDebugFlash)
			//{
			//	debugPathfinder = new NewPathFinder(map);
			//}

			foreach (var region in startingRegions)
			{
				var minPathCost = RegionMinimumPathCost(region);
				foreach (RegionLink current in region.links) 
				{
					var dist = RegionLinkDistance(rootCell, current, minPathCost);
					if (distances.ContainsKey(current))
					{
						if (dist < distances[current])
						{
							linkTargetCells[current] = GetLinkTargetCell(rootCell, current);
						}
						dist = Math.Min(distances[current], dist);
						//TODO: handle link target cell here
					}
					else
					{ linkTargetCells[current] = GetLinkTargetCell(rootCell, current); }
					distances[current] = dist;
					queue.Push(new RegionLinkQueueEntry(region, current, dist, dist));
				}
			}
		}

		public int GetRegionDistance(Region region, out RegionLink minLink)
		{
			if (regionMinLink.TryGetValue(region.id, out minLink))
			{
				return distances[minLink];
			}

			while (queue.Count != 0) {
				var vertex = queue.Pop();
				nodes_popped++;
				int knownBest = distances[vertex.Link];
				if (vertex.Cost == knownBest) //Will this ever not be true? - Yes. Not sure why. 
                {
                    var destRegion = GetLinkOtherRegion(vertex.FromRegion, vertex.Link);

					//I've never encountered this during testing, but users reported issues resolved by this check.
	                if (destRegion?.valid != true) { continue; }

                    int portalCost = 0;
					if (destRegion.portal != null)
                    {
                        //Not using Region.Allows because it is not entirely consistent with the pathfinder logic
                        //Resulting in errors when a door is within range of 22 turrets
                        portalCost = NewPathFinder.GetPathCostForBuilding(destRegion.portal, traverseParms);
                        if(portalCost < 0 ) { continue; }
                        portalCost = portalCost + OctileDistance(1, 0);
                    }

					var minPathCost = RegionMinimumPathCost(destRegion);
					foreach (var current2 in destRegion.links)
					{
						if (current2 == vertex.Link) { continue; }
						var addedCost = destRegion.portal != null ? portalCost : RegionLinkDistance(vertex.Link, current2, minPathCost);
						addedCost = Math.Max(addedCost, 1); //Handle mods with negative path costs
						int newCost = knownBest + addedCost;
                        int pathCost = RegionLinkDistance(targetCell, current2, 0) + newCost;
						int oldCost;
						if (distances.TryGetValue(current2, out oldCost))
						{
							if (newCost < oldCost)
							{
								distances[current2] = newCost;
								queue.Push(new RegionLinkQueueEntry(destRegion, current2, newCost, pathCost));
							}
						}
						else
						{
							distances.Add(current2, newCost);
							queue.Push(new RegionLinkQueueEntry(destRegion, current2, newCost, pathCost));
						}
					}
					//if this is the first vertex popped for the region, we've found the shortest path to that region
					if (!regionMinLink.ContainsKey(destRegion.id)) {
                        //if (DebugViewSettings.drawPaths && !NewPathFinder.disableDebugFlash)
                        //{
                        //    //NewPathFinder.disableDebugFlash = true;
                        //    //var tempPath = debugPathfinder?.FindPathInner(this.rootCell, new LocalTargetInfo(RegionLinkCenter(vertex.Link)), this.traverseParms, Verse.AI.PathEndMode.OnCell);
                        //    //NewPathFinder.disableDebugFlash = false;
                        //    //var actualCost = tempPath.TotalCost;
                        //    //tempPath.Dispose();
                        //    if (regionMinLink.TryGetValue(vertex.FromRegion.id, out minLink))
                        //        NewPathFinder.DebugLine(this.map, RegionLinkCenter(vertex.Link), RegionLinkCenter(minLink));
                        //    NewPathFinder.DebugFlash(this.map, RegionLinkCenter(vertex.Link), knownBest / 1500f, $"{knownBest}\n{nodes_popped}" /*+ "\n(" + actualCost + ")"*/);
                        //    //if (actualCost < knownBest) { Log.Warning(vertex.Link + " has actual cost " + actualCost + "with heuristic " + knownBest); }
                        //}
                        regionMinLink[destRegion.id] = vertex.Link;
						if (destRegion.id == region.id) {
							minLink = vertex.Link;
							return vertex.Cost;
						}
					}
				}
			}

			return 100000;
		}


		public int GetRegionBestDistances(Region region, out RegionLink bestLink, out RegionLink secondBestLink, out int secondBestCost)
		{
			int bestCost = GetRegionDistance(region, out bestLink);

			secondBestLink = null;
			secondBestCost = Int32.MaxValue;

			foreach (var link in region.links)
			{
				if (link == bestLink) { continue; }

				if (distances.ContainsKey(link))
				{
					var cost = distances[link];
					if (cost < secondBestCost)
					{
						secondBestCost = cost;
						secondBestLink = link;
					}
				}
			}

			return bestCost;
		}

		private class LinkDistanceComparer : IComparer<RegionLink>
		{
			private Dictionary<RegionLink, int> distances;

			public LinkDistanceComparer(Dictionary<RegionLink, int> distanceDict)
			{
				distances = distanceDict;
			}

			public int Compare(RegionLink a, RegionLink b)
			{
				var aDist = distances.ContainsKey(a) ? distances[a] : int.MaxValue;
				var bDist = distances.ContainsKey(b) ? distances[b] : int.MaxValue;

				return  aDist.CompareTo(bDist);
			}
		}
		//If we just use the pawn move speed as the path cost, we're effectively telling A* "there might be a road around here somewhere, keep looking!"
		//This makes it expand a whole lot more nodes than necessary in open, rough terrain, searching high and low for that alleged road.
		//Finding the minimum path cost of any tile in the region is a cheap way to guess if that road could possibly exist.
		//This could be cached across pathfinding calls, but I'd need extra detours to invalidate it and it apparently performs adequately without it.
		public int RegionMinimumPathCost(Region region)
		{
			int minCost;
			if (minPathCosts.TryGetValue(region, out minCost))
			{
				return minCost;
			}
			minCost = 10000;

			for (int z = region.extentsClose.minZ; z <= region.extentsClose.maxZ; z++) {
				for (int x = region.extentsClose.minX; x <= region.extentsClose.maxX; x++)
				{
					var index = this.map.cellIndices.CellToIndex(x, z);
					var cellCost = this.map.pathGrid.pathGrid[index] + (avoidGrid?[index]*8 ?? 0);
					if (area != null && !area[index])
					{
					    cellCost = (cellCost + pathCostSettings.moveTicksCardinal) * 20;
					}
					minCost = Math.Min(minCost, cellCost);
					if (minCost == 0)
					{
						minPathCosts[region] = 0;
						return 0;
					}
				}
			}
			minPathCosts[region] = minCost;

			return minCost;
		}

		private int RegionLinkDistance(RegionLink a, RegionLink b, int minPathCost)
		{
		    var aCell = linkTargetCells.ContainsKey(a) ? linkTargetCells[a] : RegionLinkCenter(a);
            var bCell = linkTargetCells.ContainsKey(b) ? linkTargetCells[b] : RegionLinkCenter(b);
            var diff = aCell - bCell;
            var dx = Math.Abs(diff.x);
            var dz = Math.Abs(diff.z);

            return OctileDistance(dx, dz) + minPathCost * Math.Max(dx, dz) + (int)(minPathCost * Math.Min(dx, dz) * (NewPathFinder.diagonalPercievedCostWeight - 1.0f));
		}

		private static int SpanCenterX(EdgeSpan e) => e.root.x + (e.dir == SpanDirection.East ? e.length / 2 : 0);

		private static int SpanCenterZ(EdgeSpan e) => e.root.z + (e.dir == SpanDirection.North ? e.length / 2 : 0);

		private static IntVec3 RegionLinkCenter(RegionLink link) => new IntVec3(SpanCenterX(link.span), 0, SpanCenterZ(link.span));

		private static int SpanEndX(EdgeSpan e) => e.root.x + (e.dir == SpanDirection.East ? e.length : 0);

		private static int SpanEndZ(EdgeSpan e) => e.root.z + (e.dir == SpanDirection.North ? e.length : 0);

		public static int RegionLinkCenterDistance(IntVec3 cell, RegionLink link, Func<int, int, int> cost, int minPathCost)
		{
			int dx = Math.Abs(cell.x - SpanCenterX(link.span));
			int dz = Math.Abs(cell.z - SpanCenterZ(link.span));

			return cost(dx, dz) + minPathCost * Math.Max(dx, dz);
		}

		public int RegionLinkDistance(IntVec3 cell, RegionLink link, int minPathCost)
		{
		    var targetCell = GetLinkTargetCell(cell, link);

		    var diff = cell - targetCell;
		    var dx = Math.Abs(diff.x);
            var dz = Math.Abs(diff.z);

            return OctileDistance(dx, dz) + minPathCost * Math.Max(dx, dz) + (int)(minPathCost * Math.Min(dx, dz) * (NewPathFinder.diagonalPercievedCostWeight - 1.0f));
        }

		private int OctileDistance(int dx, int dz) => (pathCostSettings.moveTicksCardinal * (dx + dz) + (pathCostSettings.moveTicksDiagonal - 2 * pathCostSettings.moveTicksCardinal) * Math.Min(dx, dz));

		private static IntVec3 GetLinkTargetCell(IntVec3 cell, RegionLink link)
	    {
	        int width = 0;
	        int height = 0;
	        if (link.span.dir == SpanDirection.North) { height = link.span.length - 1; }
	        else
	        { width = link.span.length - 1; }

	        IntVec3 targetCell = new IntVec3(Mathf.Clamp(cell.x, link.span.root.x, link.span.root.x + width), 0, Mathf.Clamp(cell.z, link.span.root.z, link.span.root.z + height));

            var diff = cell - targetCell;
            var dx = Math.Abs(diff.x);
            var dz = Math.Abs(diff.z);

            var dist = link.span.dir == SpanDirection.North ? dx : dz;
	        var factor = Math.Min(6, dist) / 6.0;
	        IntVec3 centerCell = RegionLinkCenter(link);

	        targetCell = IntVec3Lerp(targetCell, centerCell, factor);

            return targetCell;
	    }

	    private static IntVec3 IntVec3Lerp(IntVec3 cellA, IntVec3 cellB, double factor)
	    {
	        return new IntVec3((int) Math.Round(cellA.x + (cellB.x - cellA.x) * factor), 0, (int) Math.Round(cellA.z + (cellB.z - cellA.z) * factor));
	    }

	    private static Region GetLinkOtherRegion(Region fromRegion, RegionLink link) =>  Equals(fromRegion, link.RegionA) ? link.RegionB : link.RegionA;
    }
}
