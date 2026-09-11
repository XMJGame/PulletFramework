using System.Collections.Generic;
using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Pooling
{
	internal class GameObjectPool
	{
		private readonly Transform _poolRoot;
		private readonly Queue<IResourceInstanceHandle> _cacheOperations;
		private readonly HashSet<IResourceInstanceHandle> _activeOperations;
		private readonly List<IResourceInstanceHandle> _orphanedOperations;
		private readonly bool _dontDestroy;
		private readonly int _initCapacity;
		private readonly int _maxCapacity;
		private float _destroyTime;
		private float _lastRestoreRealTime = -1f;
		private bool _destroyed;

		/// <summary>
		/// 资源句柄
		/// </summary>
		public IResourceAssetHandle AssetHandle { private set; get; }

		/// <summary>
		/// 资源定位地址
		/// </summary>
		public string Location { private set; get; }

		/// <summary>
		/// 内部缓存总数
		/// </summary>
		public int CacheCount
		{
			get { return _cacheOperations.Count; }
		}

		/// <summary>
		/// 外部使用总数
		/// </summary>
		public int SpawnCount { private set; get; } = 0;

		public bool HasLoadFailed => AssetHandle != null
			&& AssetHandle.IsDone && !AssetHandle.IsSucceeded;

		/// <summary>
		/// 是否常驻不销毁
		/// </summary>
		public bool DontDestroy
		{
			get { return _dontDestroy; }
		}

		/// <summary>
		/// 销毁事件
		/// </summary>
		public float DestroyTime
		{
			set { _destroyTime = value; }
		}


		public GameObjectPool(GameObject poolRoot, string location, bool dontDestroy, int initCapacity, int maxCapacity, float destroyTime)
		{
			_poolRoot = poolRoot.transform;
			Location = location;

			_dontDestroy = dontDestroy;
			_initCapacity = initCapacity;
			_maxCapacity = maxCapacity;
			_destroyTime = destroyTime;

			// 创建缓存池
			_cacheOperations = new Queue<IResourceInstanceHandle>(initCapacity);
			_activeOperations = new HashSet<IResourceInstanceHandle>();
			_orphanedOperations = new List<IResourceInstanceHandle>();
		}

		/// <summary>
		/// 创建对象池
		/// </summary>
		public void CreatePool(IResourcePackage resourcePackage)
		{
			if (_destroyed)
				throw new System.InvalidOperationException($"Pool is destroyed: {Location}");

			// 加载游戏对象
			AssetHandle = resourcePackage.LoadAssetAsync<GameObject>(Location);
			_lastRestoreRealTime = Time.realtimeSinceStartup;

			// 创建初始对象
			for (int i = 0; i < _initCapacity; i++)
			{
				var options = new ResourceInstantiateOptions(false, _poolRoot);
				var operation = AssetHandle.InstantiateAsync(options);
				_cacheOperations.Enqueue(operation);
			}
		}

		/// <summary>
		/// 销毁游戏对象池
		/// </summary>
		public void DestroyPool()
		{
			if (_destroyed)
				return;
			_destroyed = true;

			// 先取消实例化并销毁实例，最后才能释放它们依赖的资源句柄。
			foreach (var operation in _cacheOperations)
				DestroyInstantiateOperation(operation);
			_cacheOperations.Clear();
			foreach (var operation in _activeOperations)
				DestroyInstantiateOperation(operation);
			_activeOperations.Clear();

			AssetHandle?.Release();
			AssetHandle = null;

			SpawnCount = 0;
		}

		/// <summary>
		/// 查询静默时间内是否可以销毁
		/// </summary>
		public bool CanAutoDestroy()
		{
			if (_dontDestroy)
				return false;
			if (_destroyTime < 0)
				return false;

			if (_lastRestoreRealTime > 0 && SpawnCount <= 0)
				return (Time.realtimeSinceStartup - _lastRestoreRealTime) > _destroyTime;
			else
				return false;
		}

		/// <summary>
		/// 游戏对象池是否已经销毁
		/// </summary>
		public bool IsDestroyed()
		{
			return _destroyed;
		}

		/// <summary>
		/// 回收
		/// </summary>
		public void Restore(IResourceInstanceHandle operation)
		{
			if (!ReleaseActiveOperation(operation))
			{
				return;
			}
			if (IsDestroyed())
			{
				DestroyInstantiateOperation(operation);
				return;
			}

			// 如果外部逻辑销毁了游戏对象
			if (!operation.IsSucceeded || operation.Result == null)
			{
				DestroyInstantiateOperation(operation);
				return;
			}

			// 如果缓存池还未满员
			if (_cacheOperations.Count < _maxCapacity)
			{
				SetRestoreGameObject(operation.Result);
				_cacheOperations.Enqueue(operation);
			}
			else
			{
				DestroyInstantiateOperation(operation);
			}
		}

		/// <summary>
		/// 丢弃
		/// </summary>
		public void Discard(IResourceInstanceHandle operation)
		{
			if (!ReleaseActiveOperation(operation))
				return;

			DestroyInstantiateOperation(operation);
		}

		internal void Update()
		{
			if (_destroyed || _activeOperations.Count == 0)
				return;
			_orphanedOperations.Clear();
			foreach (IResourceInstanceHandle operation in _activeOperations)
			{
				if (operation.IsDone && (!operation.IsSucceeded || operation.Result == null))
					_orphanedOperations.Add(operation);
			}
			for (int i = 0; i < _orphanedOperations.Count; i++)
			{
				IResourceInstanceHandle operation = _orphanedOperations[i];
				if (ReleaseActiveOperation(operation))
					DestroyInstantiateOperation(operation);
			}
			_orphanedOperations.Clear();
		}

		internal void SpawnFailed(IResourceInstanceHandle operation)
		{
			if (!ReleaseActiveOperation(operation))
				return;
			DestroyInstantiateOperation(operation);
		}

		/// <summary>
		/// 获取一个游戏对象
		/// </summary>
		public SpawnHandle Spawn(Transform parent, Vector3 position, Quaternion rotation, bool forceClone, params System.Object[] userDatas)
		{
			if (_destroyed || AssetHandle == null || !AssetHandle.IsValid)
				throw new System.InvalidOperationException($"Pool is unavailable: {Location}");

			IResourceInstanceHandle operation;
			if (forceClone == false && _cacheOperations.Count > 0)
				operation = _cacheOperations.Dequeue();
			else
				operation = AssetHandle.InstantiateAsync(new ResourceInstantiateOptions(false));

			_activeOperations.Add(operation);
			SpawnCount = _activeOperations.Count;
			SpawnHandle handle = new SpawnHandle(this, operation, parent, position, rotation, userDatas);
			PulletOperationSystem.Start(handle);
			return handle;
		}

		private void DestroyInstantiateOperation(IResourceInstanceHandle operation)
		{
			if (operation == null)
				return;
			// 取消异步操作
			operation.Cancel();

			// 销毁游戏对象
			if (operation.Result != null)
			{
				GameObject.Destroy(operation.Result);
			}
		}

		private bool ReleaseActiveOperation(IResourceInstanceHandle operation)
		{
			if (operation == null || !_activeOperations.Remove(operation))
				return false;
			SpawnCount = _activeOperations.Count;
			if (SpawnCount == 0)
				_lastRestoreRealTime = Time.realtimeSinceStartup;
			return true;
		}
		private void SetRestoreGameObject(GameObject gameObj)
		{
			if (gameObj != null)
			{
				gameObj.SetActive(false);
				gameObj.transform.SetParent(_poolRoot, false);
				gameObj.transform.localPosition = Vector3.zero;
				gameObj.transform.localRotation = Quaternion.identity;
			}
		}
	}
}
