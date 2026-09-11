using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace PulletFramework.Sound
{
    /// <summary>
    /// UI 按钮点击音效
    /// </summary>
    public class UIPlaySound : MonoBehaviour,
    IPointerClickHandler,
    IPointerExitHandler,
    IPointerDownHandler,
    IPointerUpHandler
    {
        public enum ETrigger
        {
            OnClick,
            OnPress,
            OnRelease
        }

        public ETrigger trigger = ETrigger.OnClick;
        public AudioClip audioClip;
        [FormerlySerializedAs("audioPath")]
        [Tooltip("YooAsset 中的音频资源定位地址。AudioClip 为空时使用。")]
        public string audioLocation = "";
        [Range(0f, 1f)]
        public float volume = 1f;
        public void OnPointerClick(PointerEventData eventData)
        {
            if (trigger == ETrigger.OnClick) Play();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (trigger == ETrigger.OnPress) Play();
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (trigger == ETrigger.OnRelease) Play();
        }
        public void OnPointerExit(PointerEventData eventData)
        {
        }

        private void Play()
        {
            if (audioClip != null) PulletSound.PlaySound(audioClip, volume);
            else if (!string.IsNullOrWhiteSpace(audioLocation)) PulletSound.PlaySound(audioLocation, volume);
        }
    }
}
