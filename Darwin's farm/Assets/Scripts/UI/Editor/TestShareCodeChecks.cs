using System;
using UnityEditor;
using UnityEngine;

public static class TestShareCodeChecks
{
    [MenuItem("Darwin's Farm/Tests/Share Code Codec")]
    public static void Run()
    {
        const string payload = "{\"day\":12,\"species\":\"鸽鸽\",\"count\":160}";
        string code = TestShareCodeCodec.Encode(payload);
        Require(code.StartsWith("DFT1.", StringComparison.Ordinal), "Code version missing");
        Require(TestShareCodeCodec.Decode(code) == payload, "Round trip changed payload");
        char[] damaged = code.ToCharArray();
        damaged[damaged.Length - 2] = damaged[damaged.Length - 2] == 'A' ? 'B' : 'A';
        bool rejected = false;
        try { TestShareCodeCodec.Decode(new string(damaged)); }
        catch (FormatException) { rejected = true; }
        Require(rejected, "Corrupted code was accepted");
        Debug.Log("TestShareCodeChecks: PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("TestShareCodeChecks: " + message);
    }
}
