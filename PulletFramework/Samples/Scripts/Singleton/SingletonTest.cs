using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using PulletFramework.Singleton;
public class SingletonTest : SingletonInstance<SingletonTest>, ISingleton
{
    public void OnCreate(object createParam)
    {
        //throw new System.NotImplementedException();
    }

    public void OnDestroy()
    {
        //throw new System.NotImplementedException();
    }

    public void OnUpdate()
    {
        //throw new System.NotImplementedException();
    }

    public void Run()
    {
        Debug.Log("SingletonTest");
    }
}
