using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PulletFramework.Machine
{
	public interface IStateNode
	{
		public void OnCreate(StateMachine machine);
		public void OnEnter(params System.Object[] datas);
		public void OnUpdate();
		public void OnExit();
	}
}
