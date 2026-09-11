using System;
using UnityEngine;
using UnityEngine.UI;
using WeChatWASM;

namespace PulletMiniGame.Platform.WeChat
{
    /// <summary>把微信开放数据域绘制的好友榜或群榜显示到指定 RawImage。</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RawImage))]
    public sealed class WeChatOpenDataLeaderboardView : MonoBehaviour
    {
        public enum ERelationScope
        {
            Friends,
            Group
        }

        [Serializable]
        private sealed class OpenDataMessage
        {
            public string type;
            public string key;
            public string shareTicket;
        }

        [SerializeField] private string leaderboardKey = "user_rank";
        [SerializeField] private ERelationScope relationScope = ERelationScope.Friends;
        [SerializeField] private string groupShareTicket;
        [SerializeField] private bool showOnEnable = true;

        private RawImage _target;
        private WXOpenDataContext _context;
        private Texture2D _placeholder;
        private bool _showRequested;
        private bool _shown;

        private void Awake()
        {
            _target = GetComponent<RawImage>();
        }

        private void OnEnable()
        {
            if (showOnEnable)
                Show();
        }

        private void OnDisable()
        {
            Hide();
        }

        private void Update()
        {
            if (_showRequested && !_shown && PulletPlatform.IsInitialized
                && PulletPlatform.Id == PulletPlatformIds.WeChat)
                ShowNow();
        }

        private void OnDestroy()
        {
            if (_placeholder != null)
                Destroy(_placeholder);
        }

        private void OnRectTransformDimensionsChange()
        {
            if (_shown)
                UpdateViewport();
        }

        public void ShowFriends(string boardId = null)
        {
            relationScope = ERelationScope.Friends;
            if (!string.IsNullOrWhiteSpace(boardId)) leaderboardKey = boardId;
            Show();
        }

        public void ShowGroup(string shareTicket, string boardId = null)
        {
            relationScope = ERelationScope.Group;
            groupShareTicket = shareTicket;
            if (!string.IsNullOrWhiteSpace(boardId)) leaderboardKey = boardId;
            Show();
        }

        public void Show()
        {
            _showRequested = true;
            if (!isActiveAndEnabled || string.IsNullOrWhiteSpace(leaderboardKey))
                return;
            if (!PulletPlatform.IsInitialized || PulletPlatform.Id != PulletPlatformIds.WeChat)
                return;
            ShowNow();
        }

        private void ShowNow()
        {
            if (relationScope == ERelationScope.Group && string.IsNullOrWhiteSpace(groupShareTicket))
            {
                PulletFramework.PLogger.Warning(
                    "[PulletMiniGame] 微信群排行需要有效的 shareTicket。");
                _showRequested = false;
                return;
            }

            _context ??= WXBase.GetOpenDataContext();
            EnsurePlaceholder();
            _shown = true;
            UpdateViewport();
            _context.PostMessage(JsonUtility.ToJson(new OpenDataMessage
            {
                type = relationScope == ERelationScope.Friends
                    ? "showFriendsRank" : "showGroupFriendsRank",
                key = leaderboardKey,
                shareTicket = groupShareTicket
            }));
        }

        public void Hide()
        {
            _showRequested = false;
            if (!_shown) return;
            _shown = false;
            WXBase.HideOpenData();
        }

        private void EnsurePlaceholder()
        {
            if (_target == null) _target = GetComponent<RawImage>();
            if (_target.texture == null)
            {
                _placeholder = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                _placeholder.name = "WeChat Open Data Placeholder";
                _target.texture = _placeholder;
            }
            // 微信 sharedCanvas 映射到 Unity 纹理后 Y 轴相反。
            _target.uvRect = new Rect(0f, 1f, 1f, -1f);
        }

        private void UpdateViewport()
        {
            if (_target == null || _target.texture == null) return;
            RectTransform rectTransform = _target.rectTransform;
            var corners = new Vector3[4];
            rectTransform.GetWorldCorners(corners);
            Canvas canvas = _target.canvas;
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera : null;
            Vector2 bottomLeft = RectTransformUtility.WorldToScreenPoint(camera, corners[0]);
            Vector2 topRight = RectTransformUtility.WorldToScreenPoint(camera, corners[2]);
            int x = Mathf.RoundToInt(bottomLeft.x);
            int y = Mathf.RoundToInt(Screen.height - topRight.y);
            int width = Mathf.Max(1, Mathf.RoundToInt(topRight.x - bottomLeft.x));
            int height = Mathf.Max(1, Mathf.RoundToInt(topRight.y - bottomLeft.y));
            WXBase.ShowOpenData(_target.texture, x, y, width, height);
        }
    }
}
