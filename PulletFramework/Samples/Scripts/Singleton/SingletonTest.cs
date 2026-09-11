using UnityEngine;
using PulletFramework.Singleton;
public class SingletonTest : SingletonInstance<SingletonTest>, ISingleton
{
    public void OnCreate(object createParam)
    {
    }

    public void OnDestroy()
    {
    }

    public void OnUpdate()
    {
    }

    public void Run()
    {
        Debug.Log("SingletonTest");
    }
}
