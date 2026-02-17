using Basis;
using Basis.Network.Core;
using Basis.Scripts.Networking.NetworkedAvatar;
using TMPro;
using UnityEngine;
using System;
using System.Collections.Generic;

public class CustomAnimation : BasisNetworkBehaviour
{
    public Animator animator;

    [SerializeField] private TMP_InputField animationInputField;

    // Cache valid bones for this avatar
    private List<HumanBodyBones> bonesToSync = new List<HumanBodyBones>();

    private void Awake()
    {
        foreach (HumanBodyBones bone in Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            if (animator.GetBoneTransform(bone) != null)
                bonesToSync.Add(bone);
        }
        Debug.Log($"[CustomAnimation] Awake: Found {bonesToSync.Count} bones to sync.");
        BasisDebug.Log($"[CustomAnimation] Awake: Found {bonesToSync.Count} bones to sync.", BasisDebug.LogTag.Networking);
    }

    private void LateUpdate()
    {
        //if (IsLocalOwner())
        //{
            List<float> boneData = new List<float>();
            foreach (var bone in bonesToSync)
            {
                Transform t = animator.GetBoneTransform(bone);
                Vector3 pos = t.localPosition;
                Quaternion rot = t.localRotation;
                boneData.Add(pos.x); boneData.Add(pos.y); boneData.Add(pos.z);
                boneData.Add(rot.x); boneData.Add(rot.y); boneData.Add(rot.z); boneData.Add(rot.w);
            }
            byte[] buffer = new byte[boneData.Count * sizeof(float)];
            Buffer.BlockCopy(boneData.ToArray(), 0, buffer, 0, buffer.Length);
            Debug.Log($"[CustomAnimation] Sending bone data, count: {boneData.Count}");
            BasisDebug.Log($"[CustomAnimation] Sending bone data, count: {boneData.Count}", BasisDebug.LogTag.Networking);
            SendCustomNetworkEvent(buffer, DeliveryMethod.ReliableOrdered);
        //}
    }

    public override void OnNetworkMessage(ushort PlayerID, byte[] buffer, DeliveryMethod DeliveryMethod)
    {
        if (buffer != null && buffer.Length > 0)
        {
            Debug.Log($"[CustomAnimation] Received bone data, length: {buffer.Length}");
            BasisDebug.Log($"[CustomAnimation] Received bone data, length: {buffer.Length}", BasisDebug.LogTag.Networking);

            int idx = 0;
            float[] boneData = new float[buffer.Length / sizeof(float)];
            Buffer.BlockCopy(buffer, 0, boneData, 0, buffer.Length);

            foreach (var bone in bonesToSync)
            {
                Transform t = animator.GetBoneTransform(bone);
                Vector3 pos = new Vector3(
                    boneData[idx], boneData[idx + 1], boneData[idx + 2]);
                Quaternion rot = new Quaternion(
                    boneData[idx + 3], boneData[idx + 4], boneData[idx + 5], boneData[idx + 6]);
                t.localPosition = pos;
                t.localRotation = rot;
                idx += 7;
            }
        }
    }

    public void ChangeAnimation(string animation, float crossFade = 0.2f)
    {
        if (IsLocalOwner())
        {
            Debug.Log($"[CustomAnimation] Playing animation: {animation}");
            BasisDebug.Log($"[CustomAnimation] Playing animation: {animation}", BasisDebug.LogTag.Networking);
            animator.CrossFade(animation, crossFade);
        }
    }

    public void OnAnimationTrigger()
    {
        Debug.Log("[CustomAnimation] OnAnimationTrigger called.");
        BasisDebug.Log("[CustomAnimation] OnAnimationTrigger called.", BasisDebug.LogTag.Networking);
        TakeOwnership();
        ChangeAnimation(animationInputField.text);
        // Bone sync will happen automatically in LateUpdate
    }

    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        Debug.Log("[CustomAnimation] OnPlayerJoined called.");
        BasisDebug.Log("[CustomAnimation] OnPlayerJoined called.", BasisDebug.LogTag.Networking);
        // Bone sync will handle pose for new players
    }
}
