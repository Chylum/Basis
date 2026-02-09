using Basis;
using Basis.Network.Core;
using Basis.Scripts.BasisSdk.Players;
using System;
using TMPro;
using UnityEngine;

public class BasisNetworkChat : BasisNetworkBehaviour
{
    [SerializeField] private TMP_InputField inputField;
    [SerializeField] private TextMeshPro playerChatText;

    public void OnSendChat()
    {
        Debug.Log($"Sending {inputField.text}");
        BasisDebug.Log($"Sending {inputField.text}", BasisDebug.LogTag.Networking);

        string message = inputField.text;
        inputField.text = "";

        // Update local text
        playerChatText.text = message;

        Debug.Log($"Sending message over network: {message}");

        // Send to network (as UTF-8 bytes)
        byte[] data = System.Text.Encoding.UTF8.GetBytes(message);
        Debug.Log(data);
        Debug.Log(BitConverter.ToString(data));
        SendCustomNetworkEvent(data, DeliveryMethod.ReliableOrdered);

        Debug.Log("Message sent.");
        BasisDebug.Log("Message sent.", BasisDebug.LogTag.Networking);
    }

    public override void OnNetworkMessage(ushort PlayerID, byte[] buffer, DeliveryMethod DeliveryMethod)
    {
        BasisDebug.Log($"Received network message from PlayerID {PlayerID}", BasisDebug.LogTag.Networking);
        Debug.Log("Received network message.");
        string message = System.Text.Encoding.UTF8.GetString(buffer);
        playerChatText.text = message;
        Debug.Log($"Updated chat text to: {message}");
        BasisDebug.Log($"Updated chat text to: {message}", BasisDebug.LogTag.Networking);
    }
}
