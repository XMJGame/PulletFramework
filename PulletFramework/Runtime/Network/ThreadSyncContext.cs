using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Threading;

namespace PulletFramework.Network
{
	public class ThreadSyncContext : SynchronizationContext
	{
		private readonly ConcurrentQueue<Action> _safeQueue = new ConcurrentQueue<Action>();

		/// <summary>
		/// 更新同步队列
		/// </summary>
		public void Update()
		{
			while (true)
			{
				if (_safeQueue.TryDequeue(out Action action) == false)
					return;
				action.Invoke();
			}
		}

		/// <summary>
		/// 向同步队列里投递一个回调方法
		/// </summary>
		public override void Post(SendOrPostCallback callback, object state)
		{
			Action action = new Action(() => { callback(state); });
			_safeQueue.Enqueue(action);
		}
	}
}
