using Game.Messages;
using Game.State;
using MapEnhancer.Native;
using Track;
using UI;
using UI.ContextMenu;
using UI.Map;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MapEnhancer
{
	public class JunctionMarker : MonoBehaviour
	{
		public static Material? matJunctionGreen;
		public static Material? matJunctionRed;

		public static JunctionMarker? junctionMarkerPrefabL;
		public static JunctionMarker? junctionMarkerPrefabR;

		//public MeshRenderer left;
		//public MeshRenderer right;
		public Image? left;
		public Image? right;
		public int id { get; private set; }
		public TrackNode? junction;
		private float lastClick;
		
		public void Start()
		{
            if (junction == null || left == null || right == null)
            {
                enabled = false;
                return;
            }
			SetColors();
			foreach (var icon in GetComponentsInChildren<MapIcon>())
			{
				icon.OnClick += OnMapMarkerPressed;
			}
			junction.OnDidChangeThrown += SetColors;
		}

		void OnMapMarkerPressed()
		{
            if (junction == null) return;
			if (Mouse.current.rightButton.wasReleasedThisFrame)
			{
				RequestSetSwitchUnlocked toggleLocked = new RequestSetSwitchUnlocked(junction.id, !junction.IsCTCSwitchUnlocked);
				if (!StateManager.CheckAuthorizedToSendMessage(toggleLocked) || !junction.IsCTCSwitch)
					return;

				UI.ContextMenu.ContextMenu shared = UI.ContextMenu.ContextMenu.Shared;
				if (UI.ContextMenu.ContextMenu.IsShown)
				{
					shared.Hide();
				}
				shared.Clear();
				shared.AddButton(ContextMenuQuadrant.Brakes, junction.IsCTCSwitchUnlocked ? "Lock Switch" : "Unlock Switch", SpriteName.Select, delegate
				{
					StateManager.ApplyLocal(toggleLocked);
				});
				shared.Show("CTC Switch");
				return;
			}

			if (Loader.Settings.DoubleClick)
			{
				if (!(Time.unscaledTime - lastClick < 0.3f))
				{
					lastClick = Time.unscaledTime;
					return;
				}
			}
			
			var setSwitch = new RequestSetSwitch(junction.id, !junction.isThrown);
			StateManager.ApplyLocal(setSwitch);
		}

		void SetColors()
		{
            if (junction == null || left == null || right == null) return;
			if (!junction.isThrown)
			{
				left.color = new Color(0f, 1f, 0f, 0.8f);
				right.color = new Color(1f, 1f, 1f, 0.5f);
			}
			else
			{
				left.color = new Color(1f, 1f, 1f, 0.5f);
				right.color = new Color(1f, 0f, 0f, 0.8f);
			}
		}

		void OnDestroy()
		{
			foreach (var icon in GetComponentsInChildren<MapIcon>())
			{
				icon.OnClick -= OnMapMarkerPressed;
			}
			if (junction != null) junction.OnDidChangeThrown -= SetColors;
		}

		public static void CreatePrefab()
		{
			if (junctionMarkerPrefabL != null)
			{
				Loader.LogDebug("Tried to make junctionMarker prefab more than once.");
				return;
			}
			var sprite = MapEnhancer.LoadTexture("arrow.png", "JunctionArrowIcon");
			MapIcon mapIcon = Instantiate<MapIcon>(TrainController.Shared.locomotiveMapIconPrefab, MapEnhancer.prefabHolder.transform);
			if (mapIcon.Text != null) DestroyImmediate(mapIcon.Text.gameObject);
			var image = mapIcon.GetComponentInChildren<Image>();
			image.sprite = sprite;
			GameObject junctionMarker = mapIcon.gameObject;
			junctionMarker.hideFlags = HideFlags.HideAndDontSave;

			junctionMarker.name = "Indicators (L)";
			var markerController = junctionMarker.AddComponent<JunctionMarker>();
			var left = image.GetComponent<RectTransform>();
			left.gameObject.name = "Indicator";
			left.pivot = new Vector2(0.5f, 1f);
			left.sizeDelta = new Vector2(50f, 50f);
			left.localScale = Vector3.one * (MapEnhancer.Instance?.Settings.JunctionMarkerScale ?? 0.6f);

			var right = Instantiate(left, junctionMarker.transform);
			right.gameObject.name = "Indicator";
			markerController.left = left.GetComponentInChildren<Image>(true);
			markerController.right = right.GetComponentInChildren<Image>(true);

			left.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
			right.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
			junctionMarkerPrefabR = Instantiate(markerController, MapEnhancer.prefabHolder.transform);
			junctionMarkerPrefabR.gameObject.hideFlags = HideFlags.HideAndDontSave;
			left.transform.localRotation = Quaternion.Euler(0f, 0f, -90f);
			right.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
			junctionMarkerPrefabR.name = "Indicators (R)";
			junctionMarkerPrefabL = markerController;
		}
	}

	public static class Tex2DExtension
	{
		public static Texture2D Circle(this Texture2D tex, int x, int y, int r, Color color)
		{
			float rSquared = r * r;

			for (int u = 0; u < tex.width; u++)
			{
				for (int v = 0; v < tex.height; v++)
				{
					if ((x - u) * (x - u) + (y - v) * (y - v) < rSquared) tex.SetPixel(u, v, color);
				}
			}

			return tex;
		}
	}
}
