using Basis;
using Basis.Network.Core;
using Basis.Scripts.Networking.NetworkedAvatar;
using System;
using TMPro;
using UnityEngine;

public class SCNE : BasisNetworkBehaviour
{
    private int counter = 0;

    [SerializeField] TextMeshPro counterText;

    public void IncrementCounter()
    {
        TakeOwnership(); // Ensure we have ownership before sending the update
        counter++;
        SendCustomNetworkEvent(BitConverter.GetBytes(counter), DeliveryMethod.ReliableOrdered);
        OnDeserialization();
    }

    public override void OnNetworkMessage(ushort playerId, byte[] buffer, DeliveryMethod deliveryMethod) // Called when a network message is received
    {
        if (buffer != null && buffer.Length >= 4)
        {
            counter = BitConverter.ToInt32(buffer, 0);
            OnDeserialization();
        }
    }

    public void OnDeserialization() // Called after any change (local or remote)
    {
        Debug.Log($"Counter value is now: {counter}");
        counterText.text = $"Counter: {counter}";
    }

    public override void OnPlayerJoined(BasisNetworkPlayer player)
    {
        if (IsLocalOwner())
        {
            SendCustomNetworkEvent(BitConverter.GetBytes(counter), DeliveryMethod.ReliableOrdered, new ushort[] { player.playerId }); // Send the current counter value to the new player
        }
    }
}
