using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Same frozen source, actual default Worker route; only raw coverage may identify Solver361.
[TestFixture]
public sealed class FixedChecksumNormalWorkerEntryTests
{
    private const string Source =
        """
        using SharpProof.Attributes;

        public static class Subject
        {
            public static int Target(int count, int a0, int a1, int a2, int a3, int a4, int a5, int a6, int a7, int a8, int a9, int a10, int a11, int a12, int a13, int a14)
            {
                Contract.Requires(count >= 0);
                Contract.Requires(count <= 64);
                Contract.Ensures(Contract.Result<int>() <= count);
                var h = -2128831035;
                // Fixed checksum round 1 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 2 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 3 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 4 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 5 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 6 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 7 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 8 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 9 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 10 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 11 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 12 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 13 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 14 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 15 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 16 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 17 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 18 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 19 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 20 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 21 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 22 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 23 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 24 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 25 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 26 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 27 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 28 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 29 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 30 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 31 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                // Fixed checksum round 32 of 32.
                h = unchecked((h ^ count) * 16777619);
                h = unchecked((h ^ a0) * 16777619);
                h = unchecked((h ^ a1) * 16777619);
                h = unchecked((h ^ a2) * 16777619);
                h = unchecked((h ^ a3) * 16777619);
                h = unchecked((h ^ a4) * 16777619);
                h = unchecked((h ^ a5) * 16777619);
                h = unchecked((h ^ a6) * 16777619);
                h = unchecked((h ^ a7) * 16777619);
                h = unchecked((h ^ a8) * 16777619);
                h = unchecked((h ^ a9) * 16777619);
                h = unchecked((h ^ a10) * 16777619);
                h = unchecked((h ^ a11) * 16777619);
                h = unchecked((h ^ a12) * 16777619);
                h = unchecked((h ^ a13) * 16777619);
                h = unchecked((h ^ a14) * 16777619);
                var i = 0;
                while (i < (h & count & a0 & a1 & a2 & a3 & a4 & a5 & a6 & a7 & a8 & a9 & a10 & a11 & a12 & a13 & a14)) { i++; }
                return i;
            }
        }

        """;

    [Test]
    public async Task FrozenChecksumUsesActualDefaultNativeWorker()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")
            ?? throw new AssertionException("The container artifact export root was not provided.");
        var output = Path.Combine(repositoryRoot, "artifacts", "coverage", "semantic", "fixed-checksum-normal-entry");
        Directory.CreateDirectory(output);
        var evidence = new OrderedEvidenceBuilder
        {
            ["sourceSha256"] = Hash(Encoding.UTF8.GetBytes(Source).ToImmutableArray()),
            ["completed"] = false,
            ["actualNativeQueryCounts"] = "NOT_EXPOSED by the normal worker API",
            ["actualConsumedResourceUnits"] = "NOT_EXPOSED by the normal worker API",
            ["actualEntryFeasibilityOutcome"] = "NOT_EXPOSED by the normal worker API",
            ["solver361ExecutionClaim"] = false
        };
        try
        {
            Assert.That(Hash(Encoding.UTF8.GetBytes(Source).ToImmutableArray()), Is.EqualTo("07E0484D84169ACBE275EBE37E512C3B3BB6BB080D8B251E47735C938698C414"));
            // The same-head strict component retains all20 real CLR+IR controls.
            // Do not repeat its48 construction probes before this normal invocation.
            var budgets = new WorkerBudgets();
            Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
            Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
            Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
            Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
            Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
            Assert.That(budgets.MaxParallelism, Is.EqualTo(4));
            using var project = WorkerTests.TestProject.Create(Source);
            Assert.That(await File.ReadAllTextAsync(project.SourcePaths.Single(), TestContext.CurrentContext.CancellationToken), Is.EqualTo(Source));
            var controlCompilation = TestCompilation.CreateExact("FixedChecksumNativeEntryOptions", [("Subject.cs", Source)]);
            var parseOptions = (CSharpParseOptions)controlCompilation.SyntaxTrees.Single().Options;
            var compilationOptions = (CSharpCompilationOptions)controlCompilation.Options;
            var ownedCompilation = project.CreateCompilation(parseOptions, compilationOptions);
            TestCompilation.AssertNoErrors(ownedCompilation);
            using var image = new MemoryStream();
            var emitted = ownedCompilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            evidence["ownedImageSha256"] = Hash(image.ToArray().ToImmutableArray());
            await File.WriteAllBytesAsync(Path.Combine(output, "owned-image.dll"), image.GetBuffer().AsMemory(0, checked((int)image.Length)));
            await File.WriteAllTextAsync(Path.Combine(output, "source.cs"), Source, new UTF8Encoding(false));
            evidence["compilationEnvironment"] = "Owned TestProject filtered references and temp document paths, not the original full-TPA artifact identity";
            var request = project.CreateRequest(cacheEnabled: false, parseOptions: parseOptions,
                compilationOptions: compilationOptions, targetFramework: "net9.0");
            Assert.That(request.Cache.Enabled, Is.False);
            Assert.That(JsonSerializer.Serialize(request.Budgets), Is.EqualTo(JsonSerializer.Serialize(budgets)));
            var requestValidation = WorkerProtocolJson.Validate(request);
            Assert.That(requestValidation.IsValid, Is.True, string.Join(", ", requestValidation.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
            await File.WriteAllTextAsync(Path.Combine(output, "request.json"), WorkerProtocolJson.SerializeRequest(request), new UTF8Encoding(false));
            var artifactBytes = (await File.ReadAllBytesAsync(request.CompilerManifest.Path, TestContext.CurrentContext.CancellationToken)).ToImmutableArray();
            Assert.That(WorkerProtocolJson.ComputeSha256(artifactBytes), Is.EqualTo(request.CompilerManifest.Sha256));
            evidence["ownedArtifactSha256"] = Hash(artifactBytes);
            await File.WriteAllBytesAsync(Path.Combine(output, "compiler-artifact.json"), artifactBytes.AsMemory());
            var snapshot = WorkerInputSnapshot.Load(request, WorkerCacheIdentity.Current, TestContext.CurrentContext.CancellationToken);
            Assert.That(snapshot.Callables, Has.Length.EqualTo(1));
            var target = snapshot.Callables.Single();
            var callableId = "M:Subject.Target(" + string.Join(",", Enumerable.Repeat("System.Int32", 16)) + ")~System.Int32";
            Assert.That(target.Entry.CallableId, Is.EqualTo(callableId));
            Assert.That(target.Total, Is.Not.Null);
            Assert.That(target.Total!.IsBodyAbstraction, Is.False);
            Assert.That(target.Total.Program.Blocks, Has.Length.EqualTo(8));
            Assert.That(target.Total.Program.Blocks.Sum(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions.Length)), Is.EqualTo(3672));
            var claim = snapshot.CompilerManifest.Manifest.Claims.Single();
            Assert.That(claim.Ordinal, Is.Zero);
            Assert.That(claim.Kind, Is.EqualTo(WorkerClaimKind.Postcondition));
            Assert.That(claim.CallableId, Is.EqualTo(callableId));
            Assert.That(Source.Substring(claim.Location.Start, claim.Location.Length), Is.EqualTo("Contract.Ensures(Contract.Result<int>() <= count)"));
            evidence["actualCallableId"] = callableId;
            evidence["actualClaimId"] = claim.ClaimId;
            evidence["expectedInputHash"] = snapshot.InputHash;
            using var worker = SharpProofWorker.Create(request.Budgets);
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            var response = await worker.VerifyAsync(request, TestContext.CurrentContext.CancellationToken);
            evidence["actualEndToEndMilliseconds"] = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var responseJson = WorkerProtocolJson.SerializeCanonicalResponse(response);
            await File.WriteAllTextAsync(Path.Combine(output, "response.json"), responseJson, new UTF8Encoding(false), TestContext.CurrentContext.CancellationToken);
            evidence["actualResponse"] = response;
            evidence["responseSha256"] = Hash(Encoding.UTF8.GetBytes(responseJson).ToImmutableArray());
            var validation = WorkerProtocolJson.Validate(response, snapshot.InputHash, snapshot.CompilerManifest.Manifest);
            evidence["validationErrors"] = validation.Errors;
            Assert.That(validation.IsValid, Is.True, string.Join(", ", validation.Errors.Select(error => error is null ? throw new System.ArgumentNullException(nameof(error)) : (error.Code))));
            Assert.That(response.RequestHash, Is.EqualTo(WorkerProtocolJson.ComputeRequestHash(request)));
            Assert.That(response.Errors, Is.Empty);
            Assert.That(response.Summary.CacheHit, Is.False);
            Assert.That(response.Summary.CacheStatus, Is.EqualTo(WorkerCacheStatus.Disabled));
            Assert.That(JsonSerializer.Serialize(response.Summary.Budgets), Is.EqualTo(JsonSerializer.Serialize(budgets)));
            Assert.That(response.CallableResults.Single().CallableId, Is.EqualTo(callableId));
            var result = response.ClaimResults.Single();
            Assert.That(result.ClaimId, Is.EqualTo(claim.ClaimId));
            Assert.That(result.Vacuity, Is.EqualTo(WorkerVacuityKind.None));
            Assert.That(result.Model, Is.Empty);
            Assert.That(result.EffectWitness, Is.Null);
            if (result.Outcome == WorkerClaimOutcome.Proven)
            {
                // Future genuine complete proof is an owned healthy success.
                Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.None));
            }
            else
            {
                Assert.That(result.Outcome, Is.EqualTo(WorkerClaimOutcome.Unknown));
                if (result.Reason == WorkerClaimReason.MethodTimeout)
                {
                    Assert.That(response.CallableResults.Single().Reason, Is.EqualTo(WorkerCallableCoverageReason.MethodTimeout));
                    Assert.That((double)evidence["actualEndToEndMilliseconds"]!, Is.GreaterThanOrEqualTo(budgets.MethodWallTimeMilliseconds));
                    // A method deadline earns no native resource-path execution credit.
                }
                else
                {
                    // Observed full-joint failure can produce SolverIncomplete;
                    // real native/query reservation exhaustion produces ResourceLimit.
                    // Refuted, arbitraryUnknown, other timeouts and infrastructure fail.
                    Assert.That(result.Reason, Is.EqualTo(WorkerClaimReason.SolverIncomplete).Or.EqualTo(WorkerClaimReason.ResourceLimit));
                }
                Assert.That(result.ProofCore, Is.Empty);
            }
            var roundTrip = WorkerProtocolJson.DeserializeResponse(responseJson)
                ?? throw new AssertionException("The actual response failed to deserialize.");
            Assert.That(WorkerProtocolJson.SerializeCanonicalResponse(roundTrip), Is.EqualTo(responseJson));
            evidence["completed"] = true;
        }
        catch (Exception exception)
        {
            evidence["failureType"] = exception.GetType().FullName;
            evidence["failureMessage"] = exception.Message;
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "observation.json"),
                evidence.ToJson() + "\n", new UTF8Encoding(false), TestContext.CurrentContext.CancellationToken);
        }
    }

    private static string Hash(ImmutableArray<byte> bytes)
    {
        if (bytes.IsDefault)
        { throw new ArgumentNullException(nameof(bytes)); }
        return Convert.ToHexString(SHA256.HashData(bytes.AsSpan()));
    }
}
