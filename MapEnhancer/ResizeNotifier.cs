using MapEnhancer.Native;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace MapEnhancer
{
	public class ResizeNotifier : MonoBehaviour
	{
		private RectTransform? _rectTransform;
		public event Action<RectTransform>? RectTransformDimensionsChanged;

		void Awake()
		{
			_rectTransform = GetComponent<RectTransform>();
		}

		void OnRectTransformDimensionsChange()
		{
			Loader.LogDebug("RectTransformDimensionsChange fired on " + this.name);
			if (_rectTransform != null && RectTransformDimensionsChanged != null)
			{
				RectTransformDimensionsChanged(_rectTransform);
			}
		}
	}
}
