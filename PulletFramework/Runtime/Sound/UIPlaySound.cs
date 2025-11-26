using UnityEngine;
using UnityEngine.EventSystems;

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
        public string audioPath = "";
        [Range(0f, 1f)]
        public float volume = 1f;
        //是否缩放
        public bool isScale = true;

        private bool IsCanPlay
        {
            get
            {
                return true;
            }
        }
        private Transform mTrans;
        private void Start()
        {
            mTrans = transform;
        }
        public void OnPointerClick(PointerEventData eventData)
        {
            if (IsCanPlay && trigger == ETrigger.OnClick)
            {
                if (audioClip != null)
                {
                    PulletSound.PlayButSound(audioClip, volume);
                }
                else if (audioPath != "")
                {
                    PulletSound.PlayButSound(audioPath, volume);
                }
            }
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isScale) return;
            //transform.DOKill();
            //transform.DOScale(0.95f, 0.01f).SetUpdate(true);
        }
        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isScale) return;
            //transform.DOKill();
            //transform.DOScale(1f, 0.01f).SetUpdate(true);
        }
        public void OnPointerExit(PointerEventData eventData)
        {
            if (!isScale) return;
            //transform.DOKill();
            //transform.DOScale(1f, 0.01f).SetUpdate(true);
        }
    }
}
