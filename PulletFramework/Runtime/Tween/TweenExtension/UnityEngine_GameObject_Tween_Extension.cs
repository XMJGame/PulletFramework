using PulletFramework.Tween;

namespace UnityEngine
{
	public static class UnityEngine_GameObject_Tween_Extension
	{
		/// <summary>
		/// 播放补间动画
		/// </summary>
		public static TweenHandle PlayTween(this GameObject go, ITweenNode tweenRoot)
		{
			return PulletTween.Play(tweenRoot, go);
		}

		/// <summary>
		/// 播放补间动画
		/// </summary>
		public static TweenHandle PlayTween(this GameObject go, ITweenChain tweenRoot)
		{
			return PulletTween.Play(tweenRoot, go);
		}

		/// <summary>
		/// 播放补间动画
		/// </summary>
		public static TweenHandle PlayTween(this GameObject go, ChainNode tweenRoot)
		{
			return PulletTween.Play(tweenRoot, go);
		}
	}
}
