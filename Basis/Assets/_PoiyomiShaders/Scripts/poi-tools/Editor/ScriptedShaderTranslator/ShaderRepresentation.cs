using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Poi.Tools.ShaderTranslator
{
    [Serializable]
    public class ShaderRepresentation
    {
        public Shader Shader { get; private set;}

        public List<ShaderProperty> Properties { get => _properties; private set => _properties = value; }
        [SerializeField] List<ShaderProperty> _properties;

        public ShaderRepresentation(Shader shader)
        {
            Shader = shader;
            Properties = new List<ShaderProperty>();
#if UNITY_6000_2_OR_NEWER
            int propertyCount = shader.GetPropertyCount();
#else
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
#endif

            for(int i = 0; i < propertyCount; ++i)
            {
                var prop = new ShaderProperty
                {
#if UNITY_6000_2_OR_NEWER
                    name = shader.GetPropertyName(i),
                    description = shader.GetPropertyDescription(i),
                    type = shader.GetPropertyType(i),
#else
                    name = ShaderUtil.GetPropertyName(shader, i),
                    description = ShaderUtil.GetPropertyDescription(shader, i),
                    type = (MaterialProperty.PropType)ShaderUtil.GetPropertyType(shader, i),
#endif
                    attributes = shader.GetPropertyAttributes(i),
                };

                ShaderProperty textureStProp = null;

                switch(prop.type)
                {
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
#else
                    case MaterialProperty.PropType.Color:
#endif
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
#else
                    case MaterialProperty.PropType.Range:
#endif
                        prop.rangeLimits = shader.GetPropertyRangeLimits(i);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
#else
                    case MaterialProperty.PropType.Vector:
#endif
                        prop.defaultVector2Value = shader.GetPropertyDefaultVectorValue(i);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
#else
                    case MaterialProperty.PropType.Float:
#endif
                        prop.defaultFloatValue = shader.GetPropertyDefaultFloatValue(i);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
#else
                    case MaterialProperty.PropType.Texture:
#endif
                        prop.defaultTextureName = shader.GetPropertyTextureDefaultName(i);
                        textureStProp = new ShaderProperty()
                        {
                            name = $"{prop.name}_ST",
                            type = prop.type,
                            description = $"{prop.description}_ST",
                        };
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                        prop.defaultIntValue = shader.GetPropertyDefaultIntValue(i);
                        break;
#elif UNITY_2022_1_OR_NEWER
                    case MaterialProperty.PropType.Int:
                        prop.defaultIntValue = shader.GetPropertyDefaultIntValue(i);
                        break;
#elif UNITY_2021_1_OR_NEWER
                    case MaterialProperty.PropType.Int:
                        prop.defaultIntValue = Convert.ToInt32(shader.GetPropertyDefaultFloatValue(i));
                        break;
#endif
                    default:
                        break;
                }

                Properties.Add(prop);
                if(textureStProp != null)
                    Properties.Add(textureStProp);
            }
        }

        public ShaderProperty this[string propertyName] => Properties.FirstOrDefault(x => x.name == propertyName);

        public Dictionary<ShaderProperty, object> GetPropertiesWithValues(Material material)
        {
            var dict = new Dictionary<ShaderProperty, object>();
            for(int i = 0; i < Properties.Count; i++)
            {
                ShaderProperty prop = Properties[i];
                switch(prop.type)
                {
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Color:
#else
                    case MaterialProperty.PropType.Color:
#endif
                        dict[prop] = material.GetColor(prop.name);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Vector:
#else
                    case MaterialProperty.PropType.Vector:
#endif
                        dict[prop] = material.GetVector(prop.name);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Texture:
#else
                    case MaterialProperty.PropType.Texture:
#endif
                        dict[prop] = material.GetTexture(prop.name);

                        // Grab the next property which should be the _ST property representing scale and offset
                        var stProp = Properties[++i];
                        var texScale = material.GetTextureScale(prop.name);
                        var texOffset = material.GetTextureOffset(prop.name);
                        dict[stProp] = new Vector4(texScale.x, texScale.y, texOffset.x, texOffset.y);
                        break;
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Int:
                        dict[prop] = material.GetInt(prop.name);
                        break;
#elif UNITY_2022_1_OR_NEWER
                    case MaterialProperty.PropType.Int:
                        dict[prop] = material.GetInt(prop.name);
                        break;
#endif
#if UNITY_6000_2_OR_NEWER
                    case UnityEngine.Rendering.ShaderPropertyType.Float:
                    case UnityEngine.Rendering.ShaderPropertyType.Range:
#else
                    case MaterialProperty.PropType.Float:
                    case MaterialProperty.PropType.Range:
#endif
                        dict[prop] = material.GetFloat(prop.name);
                        break;
                    default:
                        break;
                }
            }

            return dict;
        }
    }
}
