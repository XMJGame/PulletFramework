using UnityEngine;
using PulletFramework.Resource;

namespace PulletFramework.Pooling
{
    public class SpawnHandle : PulletAsyncOperation
    {
		private enum ESteps
		{
			None,
			Waiting,
			Done,
		}

		private readonly GameObjectPool _pool;
		private IResourceInstanceHandle _operation;
		private readonly Transform _parent;
		private readonly Vector3 _position;
		private readonly Quaternion _rotation;
		private ESteps _steps = ESteps.None;

		/// <summary>
		/// 实例化的游戏对象
		/// </summary>
		public GameObject GameObj
		{
			get
			{
				if (_operation == null)
				{
					PLogger.Warning("The spawn handle is invalid !");
					return null;
				}

				return _operation.Result;
			}
		}

		/// <summary>
		/// 用户自定义数据集
		/// </summary>
		public System.Object[] UserDatas { private set; get; }

		private SpawnHandle()
		{
		}
		internal SpawnHandle(GameObjectPool pool, IResourceInstanceHandle operation, Transform parent, Vector3 position, Quaternion rotation, params System.Object[] userDatas)
		{
			_pool = pool;
			_operation = operation;
			_parent = parent;
			_position = position;
			_rotation = rotation;
			UserDatas = userDatas;
		}
		protected override void OnStart()
		{
			_steps = ESteps.Waiting;
			OnUpdate();
		}
		protected override void OnUpdate()
		{
			if (_steps == ESteps.None || _steps == ESteps.Done)
				return;

			if (_steps == ESteps.Waiting)
			{
				if (_operation.IsDone == false)
					return;

				if (!_operation.IsSucceeded)
				{
					_steps = ESteps.Done;
					string error = _operation.Error;
					_pool.SpawnFailed(_operation);
					_operation = null;
					SetFailed(error);
					return;
				}

				if (_operation.Result == null)
				{
					_steps = ESteps.Done;
					_pool.SpawnFailed(_operation);
					_operation = null;
					SetFailed("Clone game object is null.");
					return;
				}

				// 设置参数
				_operation.Result.transform.SetParent(_parent);
				_operation.Result.transform.localPosition = _position;
				_operation.Result.transform.localRotation = _rotation;
				_operation.Result.SetActive(true);

				_steps = ESteps.Done;
				SetSucceeded();
			}
		}

		/// <summary>
		/// 回收
		/// </summary>
		public void Restore()
		{
			if (_operation != null)
			{
				IResourceInstanceHandle operation = _operation;
				_operation = null;
				_steps = ESteps.Done;
				try { _pool.Restore(operation); }
				finally { CompleteFailed("User cancelled."); }
			}
		}

		/// <summary>
		/// 丢弃
		/// </summary>
		public void Discard()
		{
			if (_operation != null)
			{
				IResourceInstanceHandle operation = _operation;
				_operation = null;
				_steps = ESteps.Done;
				try { _pool.Discard(operation); }
				finally { CompleteFailed("User cancelled."); }
			}
		}

		protected override void OnWaitForAsyncComplete()
		{
			if (_operation != null)
			{
				if (_steps == ESteps.Done)
					return;
				_operation.WaitForCompletion();
				OnUpdate();
			}
		}

        protected override void OnAbort()
        {
			if (_operation == null)
				return;
			_steps = ESteps.Done;
			_pool.Discard(_operation);
			_operation = null;
        }
    }
}
