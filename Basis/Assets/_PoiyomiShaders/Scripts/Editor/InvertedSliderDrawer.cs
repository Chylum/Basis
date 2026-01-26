using UnityEditor;
using UnityEngine;

namespace Thry.ThryEditor.Drawers
{
	public class InvertedSliderDrawer : MaterialPropertyDrawer
	{
		public override void OnGUI(Rect position, MaterialProperty prop, GUIContent label, MaterialEditor editor)
		{
			var range = prop.rangeLimits;
			// Display the positive value (negate the stored negative value)
			float displayValue = -prop.GetNumber();
			
			EditorGUI.BeginChangeCheck();
			EditorGUI.showMixedValue = prop.hasMixedValue;
			float newDisplayValue = EditorGUI.Slider(position, label, displayValue, range.x, range.y);
			EditorGUI.showMixedValue = false;
			
			if (EditorGUI.EndChangeCheck())
			{
				// Store the negative of the displayed value
				prop.SetNumber(-newDisplayValue);
			}
		}

		public override float GetPropertyHeight(MaterialProperty prop, string label, MaterialEditor editor)
		{
			ShaderProperty.RegisterDrawer(this);
			return base.GetPropertyHeight(prop, label, editor);
		}
	}
}

