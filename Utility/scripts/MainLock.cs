﻿using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.ModAPI;
using VRage;
using VRage.ModAPI;
using VRageMath;

namespace Rynchodon
{
	/// <summary>
	/// Obtains a shared lock on core thread while performing an action.
	/// </summary>
	public static class MainLock
	{
		private static Logger myLogger = new Logger("MainLock");
		private static FastResourceLock Lock_MainThread = new FastResourceLock("Lock_MainThread");
		private static FastResourceLock Lock_Lock = new FastResourceLock("Lock_Lock");

		static MainLock()
		{ Lock_MainThread.AcquireExclusive(); }

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		/// <returns>true if the exclusive lock was acquired, false if it is already held</returns>
		public static void MainThread_TryAcquireExclusive()
		{
			using (Lock_Lock.AcquireExclusiveUsing())
			{
				if (Lock_MainThread.Owned && Lock_MainThread.SharedOwners == 0)
					throw new InvalidOperationException("Exclusive lock is already held.");

				Lock_MainThread.AcquireExclusive();
			}
		}

		/// <summary>
		/// This should only ever be called from main thread.
		/// </summary>
		/// <returns>true if exclusive lock was released, false if it is not held</returns>
		public static void MainThread_TryReleaseExclusive()
		{
			using (Lock_Lock.AcquireExclusiveUsing())
			{
				if (!Lock_MainThread.Owned || Lock_MainThread.SharedOwners != 0)
					throw new InvalidOperationException("Exclusive lock is not held.");

				Lock_MainThread.ReleaseExclusive();
				return;
			}
		}

		/// <summary>
		/// perform an Action while using a shared lock on main thread.
		/// </summary>
		/// <param name="safeAction">Action to perform</param>
		public static void UsingShared(Action unsafeAction)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				unsafeAction.Invoke();
		}

		/// <summary>
		/// As UsingShared() but only performs action if no wait is required.
		/// </summary>
		/// <param name="unsafeAction">Action to perform</param>
		/// <returns>true iff unsafeAction was performed</returns>
		public static bool TryUsingShared(Action unsafeAction)
		{
			if (!Lock_MainThread.TryAcquireShared())
				return false;

			try
			{
				unsafeAction.Invoke();
				return true;
			}
			finally { Lock_MainThread.ReleaseShared(); }
		}

		public static void GetBlocks_Safe(this IMyCubeGrid grid, List<IMySlimBlock> blocks, Func<IMySlimBlock, bool> collect = null)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				grid.GetBlocks(blocks, collect);
		}

		#region IMyEntities

		public static void GetEntities_Safe(this IMyEntities entitiesObject, HashSet<IMyEntity> entities, Func<IMyEntity, bool> collect = null)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				entitiesObject.GetEntities(entities, collect);
		}

		/// <summary>
		/// <para>Uses IMyEntities.GetEntities to get entities in AABB</para>
		/// <para>Always use over GetEntitiesInAABB_Safe() when blocks are not needed, much faster.</para>
		/// </summary>
		/// <param name="preCollect">applied before intersection test</param>
		public static void GetEntitiesInAABB_Safe_NoBlock(this IMyEntities entitiesObject, BoundingBoxD boundingBox, HashSet<IMyEntity> entities, Func<IMyEntity, bool> preCollect = null)
		{
			Func<IMyEntity, bool> collector;
			if (preCollect == null)
				collector = (entity) => boundingBox.Intersects(entity.WorldAABB);
			else
				collector = (entity) => { return  preCollect(entity) && boundingBox.Intersects(entity.WorldAABB); };
			entitiesObject.GetEntities_Safe(entities, collector);
		}

		/// <summary>
		/// <para>Uses IMyEntities.GetEntities to get entities in Sphere</para>
		/// <para>Always use over GetEntitiesInSphere_Safe() when blocks are not needed, much faster.</para>
		/// </summary>
		/// <param name="preCollect">applied before intersection test</param>
		public static void GetEntitiesInSphere_Safe_NoBlock(this IMyEntities entitiesObject, BoundingSphereD boundingSphere, HashSet<IMyEntity> entities, Func<IMyEntity, bool> preCollect = null)
		{
			Func<IMyEntity, bool> collector;
			if (preCollect == null)
				collector = (entity) => boundingSphere.Intersects(entity.WorldAABB);
			else
				collector = (entity) => { return preCollect(entity) && boundingSphere.Intersects(entity.WorldAABB); };
			entitiesObject.GetEntities_Safe(entities, collector);
		}

		/// <summary>Consider using GetEntitiesInAABB_Safe_NoBlock instead.</summary>
		public static List<IMyEntity> GetEntitiesInAABB_Safe(this IMyEntities entitiesObject, ref BoundingBoxD boundingBox)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInAABB(ref boundingBox);
		}

		/// <summary>Consider using GetEntitiesInSphere_Safe_NoBlock instead.</summary>
		public static List<IMyEntity> GetEntitiesInSphere_Safe(this IMyEntities entitiesObject, ref BoundingSphereD boundingSphere)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return entitiesObject.GetEntitiesInSphere(ref boundingSphere);
		}

		#endregion

		public static MyObjectBuilder_CubeBlock GetObjectBuilder_Safe(this IMySlimBlock block)
		{
			using (Lock_MainThread.AcquireSharedUsing())
				return block.GetObjectBuilder();
		}

		public static MyObjectBuilder_CubeBlock GetSlimObjectBuilder_Safe(this IMyCubeBlock block)
		{
			using (Lock_MainThread.AcquireSharedUsing())
			{
				IMySlimBlock slim = block.CubeGrid.GetCubeBlock(block.Position);
				return slim.GetObjectBuilder();
			}
		}

		public static IMyPlayer GetPlayer_Safe(this IMyCharacter character)
		{
			List<IMyPlayer> matchingPlayer = new List<IMyPlayer>();
			using (Lock_MainThread.AcquireSharedUsing())
				MyAPIGateway.Players.GetPlayers(matchingPlayer, player => { return player.IdentityId == (character as IMyEntity).EntityId; });

			switch (matchingPlayer.Count)
			{
				case 0:
					return null;
				case 1:
					return matchingPlayer[0];
				default:
					VRage.Exceptions.ThrowIf<InvalidOperationException>(true, "too many matching players (" + matchingPlayer.Count + ")");
					return null;
			}
		}

		public static List<IMyVoxelBase> GetInstances_Safe(this IMyVoxelMaps mapsObject, Func<IMyVoxelBase, bool> collect = null)
		{
			List<IMyVoxelBase> outInstances = new List<IMyVoxelBase>();
			using (Lock_MainThread.AcquireSharedUsing())
				mapsObject.GetInstances(outInstances, collect);
			return outInstances;
		}

		/// <remarks>I have not tested IsInsideVoxel for thread-safety, I assumed it is not.</remarks>
		public static bool RayCastVoxel(this IMyEntities entities, Vector3 from, Vector3 to, out Vector3 boundary)
		{
			using (Lock_MainThread.AcquireSharedUsing())
			{
				entities.IsInsideVoxel(from, to, out boundary);
				return (boundary != from);
			}
		}
	}
}
