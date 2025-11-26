using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Machine
{
	public class StateMachine
	{
		private readonly Dictionary<string, IStateNode> _nodes = new Dictionary<string, IStateNode>(100);
		private IStateNode _curNode;
		private IStateNode _preNode;

		public IStateNode CurNode { get { return _curNode; } }

		/// <summary>
		/// 状态机持有者
		/// </summary>
		public System.Object Owner { private set; get; }

		/// <summary>
		/// 当前运行的节点名称
		/// </summary>
		public string CurrentNode
		{
			get { return _curNode != null ? _curNode.GetType().FullName : string.Empty; }
		}

		/// <summary>
		/// 之前运行的节点名称
		/// </summary>
		public string PreviousNode
		{
			get { return _preNode != null ? _preNode.GetType().FullName : string.Empty; }
		}


		private StateMachine() { }
		public StateMachine(System.Object owner)
		{
			Owner = owner;
		}

		/// <summary>
		/// 更新状态机
		/// </summary>
		public void Update()
		{
			if (_curNode != null)
				_curNode.OnUpdate();
		}

		/// <summary>
		/// 启动状态机
		/// </summary>
		public void Run<TNode>(params System.Object[] userDatas) where TNode : IStateNode
		{
			var nodeType = typeof(TNode);
			var nodeName = nodeType.FullName;
			Run(nodeName, userDatas);
		}
		public void Run(Type entryNode, params System.Object[] userDatas)
		{
			var nodeName = entryNode.FullName;
			Run(nodeName, userDatas);
		}
		public void Run(string entryNode, params System.Object[] userDatas)
		{
			_curNode = TryGetNode(entryNode);
			_preNode = _curNode;

			if (_curNode == null)
				throw new Exception($"Not found entry node: {entryNode }");

			_curNode.OnEnter(userDatas);
		}

		/// <summary>
		/// 加入一个节点
		/// </summary>
		public void AddNode<TNode>() where TNode : IStateNode
		{
			var nodeType = typeof(TNode);
			var stateNode = Activator.CreateInstance(nodeType) as IStateNode;
			AddNode(stateNode);
		}
		public void AddNode(IStateNode stateNode)
		{
			if (stateNode == null)
				throw new ArgumentNullException();

			var nodeType = stateNode.GetType();
			var nodeName = nodeType.FullName;

			if (_nodes.ContainsKey(nodeName) == false)
			{
				stateNode.OnCreate(this);
				_nodes.Add(nodeName, stateNode);
			}
			else
			{
				PLogger.Error($"State node already existed : {nodeName}");
			}
		}

		/// <summary>
		/// 转换状态节点
		/// </summary>
		public void ChangeState<TNode>(params System.Object[] userDatas) where TNode : IStateNode
		{
			var nodeType = typeof(TNode);
			var nodeName = nodeType.FullName;
			ChangeState(nodeName, userDatas);
		}
		public void ChangeState(Type nodeType, params System.Object[] userDatas)
		{
			var nodeName = nodeType.FullName;
			ChangeState(nodeName,userDatas);
		}
		public void ChangeState(string nodeName, params System.Object[] userDatas)
		{
			if (string.IsNullOrEmpty(nodeName))
				throw new ArgumentNullException();

			IStateNode node = TryGetNode(nodeName);
			if (node == null)
			{
				PLogger.Error($"Can not found state node : {nodeName}");
				return;
			}

			PLogger.Log($"{_curNode.GetType().FullName} --> {node.GetType().FullName}");
			_preNode = _curNode;
			_curNode.OnExit();
			_curNode = node;
			_curNode.OnEnter(userDatas);
		}

		public IStateNode GetNode<TNode>() where TNode : IStateNode 
		{
			var nodeType = typeof(TNode);
			var nodeName = nodeType.FullName;
			return TryGetNode(nodeName);
		}

		private IStateNode TryGetNode(string nodeName)
		{
			_nodes.TryGetValue(nodeName, out IStateNode result);
			return result;
		}
	}
}
