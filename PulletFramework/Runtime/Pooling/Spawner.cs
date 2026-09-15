using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Pooling
{
    public class Spawner
    {
        private readonly List<GameObjectPool> _GameObjectPools = new List<GameObjectPool>(100);
        private readonly List<GameObjectPool> _RemoveList = new List<GameObjectPool>(100);
        private readonly Dictionary<string, GameObjectPool> _PoolsByLocation =
            new Dictionary<string, GameObjectPool>(StringComparer.Ordinal);
        private readonly GameObject _SpawnerRoot;
        private readonly IResourcePackage _resourcePackage;
        private readonly string _packageName;
        private bool _destroyed;

        public string packageName
        {
            get
            {
                return _packageName;
            }
        }


        private Spawner()
        {
        }
        internal Spawner(GameObject poolingRoot, IResourcePackage resourcePackage)
        {
            _SpawnerRoot = new GameObject(resourcePackage.Name);
            _SpawnerRoot.transform.SetParent(poolingRoot.transform);
            _SpawnerRoot.SetActive(false);
            _resourcePackage = resourcePackage;
            _packageName = resourcePackage.Name;
        }

        /// <summary>
        /// 更新游戏对象池系统
        /// </summary>
        internal void Update()
        {
            if (_destroyed)
                return;
            _RemoveList.Clear();
            foreach (var pool in _GameObjectPools)
            {
				pool.Update();
                if (pool.CanAutoDestroy())
                    _RemoveList.Add(pool);
            }

            foreach (var pool in _RemoveList)
            {
                _GameObjectPools.Remove(pool);
                _PoolsByLocation.Remove(pool.Location);
                pool.DestroyPool();
            }
        }

        /// <summary>
        /// 销毁游戏对象池系统
        /// </summary>
        internal void Destroy()
        {
            if (_destroyed)
                return;
            _destroyed = true;
            DestroyPools(true);
			if (_SpawnerRoot != null)
				UnityEngine.Object.Destroy(_SpawnerRoot);
        }

		public int PoolCount => _GameObjectPools.Count;
		public bool IsDestroyed => _destroyed;

        /// <summary>
        /// 销毁所有对象池及其资源
        /// </summary>
        /// <param name="includeAll">销毁所有对象池，包括常驻对象池</param>
        public void DestroyAll(bool includeAll)
        {
            if (_destroyed)
                return;
            DestroyPools(includeAll);
        }

        private void DestroyPools(bool includeAll)
        {
            if (includeAll)
            {
                foreach (var pool in _GameObjectPools)
                {
                    pool.DestroyPool();
                }
                _GameObjectPools.Clear();
                _PoolsByLocation.Clear();
            }
            else
            {
				_RemoveList.Clear();
                foreach (var pool in _GameObjectPools)
                {
                    if (pool.DontDestroy == false)
                        _RemoveList.Add(pool);
                }
                foreach (var pool in _RemoveList)
                {
                    _GameObjectPools.Remove(pool);
                    _PoolsByLocation.Remove(pool.Location);
                    pool.DestroyPool();
                }
				_RemoveList.Clear();
            }
        }


        /// <summary>
        /// 异步创建指定资源的游戏对象池
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="dontDestroy">资源常驻不销毁</param>
        /// <param name="initCapacity">对象池的初始容量</param>
        /// <param name="maxCapacity">对象池的最大容量</param>
        /// <param name="destroyTime">静默销毁时间（注意：小于零代表不主动销毁）</param>
        public CreatePoolOperation CreateGameObjectPoolAsync(string location, bool dontDestroy = false, int initCapacity = 0, int maxCapacity = int.MaxValue, float destroyTime = -1f)
        {
            return CreateGameObjectPoolInternal(location, dontDestroy, initCapacity, maxCapacity, destroyTime);
        }

        /// <summary>
        /// 同步创建指定资源的游戏对象池
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="dontDestroy">资源常驻不销毁</param>
        /// <param name="initCapacity">对象池的初始容量</param>
        /// <param name="maxCapacity">对象池的最大容量</param>
        /// <param name="destroyTime">静默销毁时间（注意：小于零代表不主动销毁）</param>
        public CreatePoolOperation CreateGameObjectPoolSync(string location, bool dontDestroy = false, int initCapacity = 0, int maxCapacity = int.MaxValue, float destroyTime = -1f)
        {
            var operation = CreateGameObjectPoolInternal(location, dontDestroy, initCapacity, maxCapacity, destroyTime);
            operation.WaitForAsyncComplete();
            return operation;
        }

        /// <summary>
        /// 创建指定资源的游戏对象池
        /// </summary>
        private CreatePoolOperation CreateGameObjectPoolInternal(string location, bool dontDestroy = false, int initCapacity = 0, int maxCapacity = int.MaxValue, float destroyTime = -1f)
        {
			EnsureAvailable();
			if (string.IsNullOrWhiteSpace(location))
				throw new ArgumentException("Pool location is required.", nameof(location));
			if (initCapacity < 0)
				throw new ArgumentOutOfRangeException(nameof(initCapacity));
			if (maxCapacity < 0)
				throw new ArgumentOutOfRangeException(nameof(maxCapacity));
            if (maxCapacity < initCapacity)
                throw new Exception("The max capacity value must be greater the init capacity value.");

            GameObjectPool pool = TryGetGameObjectPool(location);
			if (pool != null && pool.HasLoadFailed)
			{
				_GameObjectPools.Remove(pool);
				_PoolsByLocation.Remove(location);
				pool.DestroyPool();
				pool = null;
			}
            if (pool != null)
            {
                PLogger.Warning($"GameObject pool is already existed : {location}");
                var operation = new CreatePoolOperation(pool.AssetHandle);
                PulletOperationSystem.Start(operation);
                return operation;
            }
            else
            {
                pool = new GameObjectPool(_SpawnerRoot, location, dontDestroy, initCapacity, maxCapacity, destroyTime);
                pool.CreatePool(_resourcePackage);
                _GameObjectPools.Add(pool);
                _PoolsByLocation.Add(location, pool);

                var operation = new CreatePoolOperation(pool.AssetHandle);
                PulletOperationSystem.Start(operation);
                return operation;
            }
        }


        /// <summary>
        /// 异步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnAsync(string location, bool forceClone = false, params System.Object[] userDatas)
        {
            return SpawnInternal(location, null, Vector3.zero, Quaternion.identity, forceClone, userDatas);
        }

        /// <summary>
        /// 异步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnAsync(string location, Transform parent, bool forceClone = false, params System.Object[] userDatas)
        {
            return SpawnInternal(location, parent, Vector3.zero, Quaternion.identity, forceClone, userDatas);
        }

        /// <summary>
        /// 异步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="position">相对父物体的本地坐标</param>
        /// <param name="rotation">相对父物体的本地旋转</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnAsync(string location, Transform parent, Vector3 position, Quaternion rotation, bool forceClone = false, params System.Object[] userDatas)
        {
            return SpawnInternal(location, parent, position, rotation, forceClone, userDatas);
        }

        /// <summary>
        /// 同步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnSync(string location, bool forceClone = false, params System.Object[] userDatas)
        {
            SpawnHandle handle = SpawnInternal(location, null, Vector3.zero, Quaternion.identity, forceClone, userDatas);
            handle.WaitForAsyncComplete();
            return handle;
        }

        /// <summary>
        /// 同步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnSync(string location, Transform parent, bool forceClone = false, params System.Object[] userDatas)
        {
            SpawnHandle handle = SpawnInternal(location, parent, Vector3.zero, Quaternion.identity, forceClone, userDatas);
            handle.WaitForAsyncComplete();
            return handle;
        }

        /// <summary>
        /// 同步实例化一个游戏对象
        /// </summary>
        /// <param name="location">资源定位地址</param>
        /// <param name="parent">父物体</param>
        /// <param name="position">相对父物体的本地坐标</param>
        /// <param name="rotation">相对父物体的本地旋转</param>
        /// <param name="forceClone">强制克隆游戏对象，忽略缓存池里的对象</param>
        /// <param name="userDatas">用户自定义数据</param>
        public SpawnHandle SpawnSync(string location, Transform parent, Vector3 position, Quaternion rotation, bool forceClone = false, params System.Object[] userDatas)
        {
            SpawnHandle handle = SpawnInternal(location, parent, position, rotation, forceClone, userDatas);
            handle.WaitForAsyncComplete();
            return handle;
        }

        /// <summary>
        /// 实例化一个游戏对象
        /// </summary>
        private SpawnHandle SpawnInternal(string location, Transform parent, Vector3 position, Quaternion rotation, bool forceClone, params System.Object[] userDatas)
        {
			EnsureAvailable();
			if (string.IsNullOrWhiteSpace(location))
				throw new ArgumentException("Pool location is required.", nameof(location));
            var pool = TryGetGameObjectPool(location);
			if (pool != null && pool.HasLoadFailed)
			{
				_GameObjectPools.Remove(pool);
				_PoolsByLocation.Remove(location);
				pool.DestroyPool();
				pool = null;
			}
            if (pool != null)
            {
                return pool.Spawn(parent, position, rotation, forceClone, userDatas);
            }

            // 如果不存在创建游戏对象池
            pool = new GameObjectPool(_SpawnerRoot, location, false, 0, int.MaxValue, -1f);
            pool.CreatePool(_resourcePackage);
            _GameObjectPools.Add(pool);
            _PoolsByLocation.Add(location, pool);
            return pool.Spawn(parent, position, rotation, forceClone, userDatas);
        }

        public void DestroyGameObjectPool(string location)
        {
			if (_destroyed)
				return;
            var pool = TryGetGameObjectPool(location);
            if (pool != null)
            {
                _GameObjectPools.Remove(pool);
                _PoolsByLocation.Remove(location);
                pool.DestroyPool();
            }
        }

        private GameObjectPool TryGetGameObjectPool(string location)
        {
            return location != null && _PoolsByLocation.TryGetValue(location, out GameObjectPool pool)
                ? pool
                : null;
        }

		private void EnsureAvailable()
		{
			if (_destroyed)
				throw new ObjectDisposedException(
					nameof(Spawner), $"Spawner for package '{_packageName}' is destroyed.");
		}
    }
}
