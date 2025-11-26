#region Copyright (C) 
// ********************************************************************
//  Copyright (C) 2020-2024 Tianzhuo Vision Vreation Technology(Beijing) Co., Ltd. All Rights Reserved.
//  作    者：许明俊
//  创建日期：2022
//  功能描述：PulletFramework 框架 - 事件组系统
//
// *********************************************************************
#endregion
using System;
using System.Collections.Generic;

namespace PulletFramework.Event
{
    public class EventGroup
    {
		private readonly Dictionary<System.Type, List<Action<IEventMessage>>> _CachedListener = new Dictionary<System.Type, List<Action<IEventMessage>>>();

		/// <summary>
		/// 添加一个监听
		/// </summary>
		public void AddListener<TEvent>(System.Action<IEventMessage> listener) where TEvent : IEventMessage
		{
			System.Type eventType = typeof(TEvent);
			if (_CachedListener.ContainsKey(eventType) == false)
				_CachedListener.Add(eventType, new List<Action<IEventMessage>>());

			if (_CachedListener[eventType].Contains(listener) == false)
			{
				_CachedListener[eventType].Add(listener);
				PulletEvent.AddListener(eventType, listener);
			}
			else
			{
				PLogger.Warning($"Event listener is exist : {eventType}");
			}
		}

		/// <summary>
		/// 移除所有缓存的监听
		/// </summary>
		public void RemoveAllListener()
		{
			foreach (var pair in _CachedListener)
			{
				System.Type eventType = pair.Key;
				for (int i = 0; i < pair.Value.Count; i++)
				{
					PulletEvent.RemoveListener(eventType, pair.Value[i]);
				}
				pair.Value.Clear();
			}
			_CachedListener.Clear();
		}
	}
}
