using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Poi.Tools.ShaderTranslator.VersionUpgrade
{
	public interface IPoiyomiVersionUpgrade
	{
		Version GetSourceVersion();
		Version GetTargetVersion();
	}

	public static class PoiyomiVersionUpgradeController
	{
		static readonly List<ScriptedShaderTranslator> Translators = new List<ScriptedShaderTranslator>
		{
			new PoiyomiUpgrade_9_1_to_9_2(),
			new PoiyomiUpgrade_9_2_to_9_3(),
			new PoiyomiUpgrade_9_3_to_10_0()
		};

		public static bool UpgradeToLatest(Material material, bool deferFinalShaderSwap, out Shader finalShader)
		{
			finalShader = null;

			if (material == null)
				return false;

			if (!PoiyomiVersionDetector.IsPoiyomiShader(material))
			{
				Debug.LogWarning($"Material <b>{material.name}</b> is not using a Poiyomi shader.");
				return false;
			}

			if (!PoiyomiVersionDetector.TryGetVersion(material, out Version startVersion))
			{
				Debug.LogWarning($"Could not detect version for material <b>{material.name}</b>.");
				return false;
			}

			if (startVersion >= PoiyomiVersionDetector.LatestVersion)
			{
				Debug.Log($"Material <b>{material.name}</b> is already on the latest version ({startVersion}).");
				return false;
			}

			Debug.Log($"Upgrading material <b>{material.name}</b> from version {startVersion} to {PoiyomiVersionDetector.LatestVersion}");

			// First pass: collect all translators we'll need and find final shader
			var translatorChain = new List<ScriptedShaderTranslator>();
			Version simVersion = startVersion;

			while (simVersion < PoiyomiVersionDetector.LatestVersion)
			{
				ScriptedShaderTranslator translator = FindTranslatorForVersion(simVersion);
				if (translator == null)
					break;

				translatorChain.Add(translator);
				finalShader = translator.ResolveTargetShader(material, string.Empty);
				simVersion = ((IPoiyomiVersionUpgrade)translator).GetTargetVersion();
			}

			if (translatorChain.Count == 0)
			{
				Debug.LogWarning($"No translators available for material <b>{material.name}</b> at version {startVersion}.");
				return false;
			}

			// Run all translations with deferred shader swap to avoid repeated compilation
			foreach (var translator in translatorChain)
				translator.Translate(material, string.Empty, true);

			// Apply final shader swap unless caller wants to handle it
			if (!deferFinalShaderSwap && finalShader != null)
			{
				ScriptedShaderTranslator.ApplyDeferredShaderSwap(material, finalShader);
				Debug.Log($"Material <b>{material.name}</b> upgraded to version {PoiyomiVersionDetector.LatestVersion}");
			}

			return true;
		}

		public static bool UpgradeToLatest(Material material) => UpgradeToLatest(material, false, out _);

		static ScriptedShaderTranslator FindTranslatorForVersion(Version version)
		{
			foreach (var translator in Translators)
			{
				if (translator is IPoiyomiVersionUpgrade upgrade)
				{
					var src = upgrade.GetSourceVersion();
					if (version.Major == src.Major && version.Minor == src.Minor)
						return translator;
				}
			}
			return null;
		}

		public static void UpgradeMaterials(IEnumerable<Material> materials)
		{
			var materialList = new List<Material>(materials);
			var pendingSwaps = new List<(Material mat, Shader shader)>();

			try
			{
				for (int i = 0; i < materialList.Count; i++)
				{
					var material = materialList[i];
					if (material == null)
						continue;

					EditorUtility.DisplayProgressBar("Upgrading Materials",
						$"Processing {material.name} ({i + 1}/{materialList.Count})",
						(float)i / materialList.Count);

					if (UpgradeToLatest(material, true, out Shader finalShader) && finalShader != null)
						pendingSwaps.Add((material, finalShader));
				}

				EditorUtility.DisplayProgressBar("Upgrading Materials",
					$"Applying shader swaps ({pendingSwaps.Count} materials)",
					0.95f);

				// Apply all shader swaps at the end to avoid repeated compilation
				foreach (var (mat, shader) in pendingSwaps)
				{
					ScriptedShaderTranslator.ApplyDeferredShaderSwap(mat, shader);
					Debug.Log($"Applied deferred shader swap for <b>{mat.name}</b>");
				}
			}
			finally
			{
				EditorUtility.ClearProgressBar();
			}

			Debug.Log($"Upgraded {pendingSwaps.Count} materials");
		}
	}
}
