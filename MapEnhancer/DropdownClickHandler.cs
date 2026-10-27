using Model;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MapEnhancer
{
	internal class DropdownClickHandler : MonoBehaviour, IPointerDownHandler
	{
		public List<Car> cars = new List<Car>();
		private TMP_Dropdown? dropdown;

		public void Awake()
		{
			dropdown = GetComponent<TMP_Dropdown>();
		}

		public void OnPointerDown(PointerEventData eventData)
		{
			if (dropdown == null || dropdown.IsExpanded) return;
			dropdown.ClearOptions();
			cars = TrainController.Shared.Cars.Where((Car car) => car.IsLocomotive).OrderBy(car => car.Ident.RoadNumber.Length).ThenBy(car => car.Ident.RoadNumber).ToList();
			dropdown.AddOptions(cars.Select(car =>
			{
				if (string.IsNullOrEmpty(car.DefinitionInfo.Metadata.Name)) return "";
				var splitName = car.DefinitionInfo.Metadata.Name.Split();
				var name = splitName[0] == "EMD" ? splitName[1] : splitName[0];
				var typeName = car.DisplayName.Length > 9 ? "" : $"({name})";
				return $"{car.DisplayName}<pos=70%>{typeName}";
			}).Prepend("Locomotive...").ToList());
		}
	}
}
