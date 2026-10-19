using System.Collections.Immutable;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;
using SharpProof.Attributes;
using SharpProof.CompilerArtifact;
using SharpProof.Ir;
using SharpProof.Worker.Protocol;

namespace SharpProof.Worker.Test;

// Prospective fixed construction-boundary regression. No native/Houdini execution claim.
[TestFixture]
public sealed class FixedChecksumAnchorConstructionBoundaryTests
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
    public async Task OwnedChecksumProgramAdmitsSingletonPlansButExceedsJointConstructionLimit()
    {
        var repositoryRoot = Environment.GetEnvironmentVariable("SHARPPROOF_REPO_ROOT")
            ?? throw new AssertionException("The container artifact export root was not provided.");
        var output = Path.Combine(repositoryRoot, "artifacts", "coverage", "semantic", "fixed-checksum-construction");
        Directory.CreateDirectory(output);
        var observation = new Dictionary<string, object?>
        {
            ["sourceSha256"] = Hash(Encoding.UTF8.GetBytes(Source)),
            ["nativeQueryExecutionRequested"] = false,
            ["disposition"] = "Not completed",
            ["defaultInterpreterStepLimit"] = 10_000,
            ["interpretation"] = "Generated candidates are unproven; construction is not Solver361 execution"
        };
        try
        {
            await File.WriteAllTextAsync(Path.Combine(output, "source.cs"), Source, new UTF8Encoding(false));
            var compilation = TestCompilation.Create("FixedChecksumAnchorConstruction", ("Subject.cs", Source));
            TestCompilation.AssertNoErrors(compilation);
            using var image = new MemoryStream();
            var emitted = compilation.Emit(image);
            Assert.That(emitted.Success, Is.True, string.Join("\n", emitted.Diagnostics));
            observation["imageSha256"] = Hash(image.GetBuffer().AsSpan(0, checked((int)image.Length)));
            await File.WriteAllBytesAsync(Path.Combine(output, "compiler-image.dll"), image.GetBuffer().AsMemory(0, checked((int)image.Length)));
            // The runtime image has the same source; only the shipped Conditional symbol is absent.
            var runtimeTrees = compilation.SyntaxTrees.Select(tree => tree is null ? throw new System.ArgumentNullException(nameof(tree)) : (CSharpSyntaxTree.ParseText(tree.GetText(),
                ((CSharpParseOptions)tree.Options).WithPreprocessorSymbols(
                    ((CSharpParseOptions)tree.Options).PreprocessorSymbolNames.Where(symbol => symbol is null ? throw new System.ArgumentNullException(nameof(symbol)) : (symbol != Contract.ConditionalSymbol))), tree.FilePath)));
            var runtimeCompilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(runtimeTrees);
            TestCompilation.AssertNoErrors(runtimeCompilation);
            using var runtimeImage = new MemoryStream();
            var runtimeEmitted = runtimeCompilation.Emit(runtimeImage);
            Assert.That(runtimeEmitted.Success, Is.True, string.Join("\n", runtimeEmitted.Diagnostics));
            observation["runtimeImageSha256"] = Hash(runtimeImage.GetBuffer().AsSpan(0, checked((int)runtimeImage.Length)));
            var runtimeTarget = Assembly.Load(runtimeImage.ToArray()).GetType("Subject")!.GetMethod("Target")!;
            Assert.That(runtimeTarget.GetParameters(), Has.Length.EqualTo(16));
            var controls = Controls();
            var controlResults = new List<object>();
            foreach (var inputs in controls)
            {
                var checksum = ReferenceChecksum(inputs);
                var mask = inputs.Aggregate(-1, (current, word) => current & word);
                var expected = unchecked((int)checksum) & mask;
                var actual = (int)runtimeTarget.Invoke(null, inputs.Cast<object>().ToArray())!;
                controlResults.Add(new { inputs, checksum, preChecksumMask = mask, expected, actual });
                observation["controls"] = controlResults;
                Assert.That(actual, Is.EqualTo(expected));
                Assert.That(actual, Is.InRange(0, inputs[0]));
            }
            Assert.That(controls, Has.Length.EqualTo(20));
            var baseline = controls[4];
            var changed = controls[5];
            Assert.That(baseline[0], Is.EqualTo(64));
            Assert.That(changed[0], Is.EqualTo(64));
            Assert.That(baseline.Aggregate(-1, (value, word) => value & word), Is.EqualTo(64));
            Assert.That(changed.Aggregate(-1, (value, word) => value & word), Is.EqualTo(64));
            Assert.That(ReferenceChecksum(baseline), Is.EqualTo(0xc21d69c5U));
            Assert.That(ReferenceChecksum(changed), Is.EqualTo(0xdbd30b05U));
            Assert.That(runtimeTarget.Invoke(null, baseline.Cast<object>().ToArray()), Is.EqualTo(64));
            Assert.That(runtimeTarget.Invoke(null, changed.Cast<object>().ToArray()), Is.EqualTo(0));

            var budgets = new WorkerBudgets();
            Assert.That(budgets.QueryRlimit, Is.EqualTo(3_000_000));
            Assert.That(budgets.MethodRlimit, Is.EqualTo(20_000_000));
            Assert.That(budgets.MaximumExpressionDepth, Is.EqualTo(64));
            Assert.That(budgets.MethodWallTimeMilliseconds, Is.EqualTo(10_000));
            Assert.That(budgets.ProjectWallTimeMilliseconds, Is.EqualTo(300_000));
            var artifact = CompilerManifestArtifactProducer.Create(compilation, "/project", "net9.0",
                WorkerFeatureSet.All, new ClaimManifestBuilder(compilation).Build(),
                WorkerBudgets.DefaultMaximumExpressionDepth, TestContext.CurrentContext.CancellationToken);
            var artifactJson = CompilerManifestArtifactJson.SerializeProducerValidated(artifact, cancellationToken: TestContext.CurrentContext.CancellationToken);
            observation["artifactSha256"] = Hash(Encoding.UTF8.GetBytes(artifactJson));
            await File.WriteAllTextAsync(Path.Combine(output, "artifact.json"), artifactJson, new UTF8Encoding(false));
            CompilerManifestArtifactJson.DeserializePrepared(artifactJson, out var preparations, cancellationToken: TestContext.CurrentContext.CancellationToken);
            var preparation = preparations.Single();
            var expectedId = "M:Subject.Target(" + string.Join(",", Enumerable.Repeat("System.Int32", 16)) + ")~System.Int32";
            Assert.That(preparation.Entry.CallableId, Is.EqualTo(expectedId));
            observation["callableId"] = preparation.Entry.CallableId;
            observation["preparationFailureReason"] = preparation.FailureReason.ToString();
            var candidate = PassiveCallableArtifactAdapter.Enroll(preparation);
            observation["candidateAvailable"] = candidate != null;
            observation["bodyAbstraction"] = candidate?.IsBodyAbstraction;
            if (candidate == null || candidate.IsBodyAbstraction)
            {
                throw new AssertionException("The prospective real compiler-owned Total body must be available and non-abstract.");
            }
            Assert.That(candidate.Program, Is.SameAs(preparation.Total!.Program));
            Assert.That(candidate.Factory, Is.SameAs(preparation.Total.Program.Factory));
            Assert.That(candidate.Parameters, Has.Length.EqualTo(16));
            Assert.That(candidate.Requires, Has.Length.EqualTo(2));
            Assert.That(candidate.Ensures, Has.Length.EqualTo(1));
            var irControlCount = 0;
            foreach (var inputs in controls)
            {
                var initial = global::System.Collections.Immutable.ImmutableDictionary<IrVarId, IrValue>.Empty;
                for (var ordinal = 0; ordinal < inputs.Length; ordinal++)
                {
                    var binding = candidate.Parameters[ordinal];
                    initial = initial.Add(binding.Entry, candidate.Factory.CreateIntegerValue(
                        candidate.Factory.GetVariableInfo(binding.Entry).Type, inputs[ordinal]));
                }
                // Public interpreter default10000, not an expanded limit or Worker replay4096 override.
                var replay = new IrProgramInterpreter(candidate.Factory).Execute(candidate.Program, initial);
                Assert.That(replay.Status, Is.EqualTo(IrProgramExecutionStatus.Returned));
                Assert.That(replay.ConsumedApproximation, Is.False);
                Assert.That(replay.Unsupported, Is.Null);
                Assert.That(replay.Exception, Is.Null);
                Assert.That(replay.ReturnValue, Is.Not.Null);
                Assert.That(replay.ReturnValue!.IntegerNumericValue,
                    Is.EqualTo(new System.Numerics.BigInteger(unchecked((int)ReferenceChecksum(inputs)) & inputs.Aggregate(-1, (value, word) => value & word))));
                irControlCount++;
                observation["irControlCount"] = irControlCount;
            }
            observation["originalBlocks"] = candidate.Program.Blocks.Length;
            observation["originalInstructions"] = candidate.Program.Blocks.Sum(block => block is null ? throw new System.ArgumentNullException(nameof(block)) : (block.Instructions.Length));
            var loops = PassiveLoopCutter.Loops(candidate, TestContext.CurrentContext.CancellationToken);
            var printer = new IrPrinter(candidate.Factory);
            observation["loops"] = loops.Select(loop => loop is null ? throw new System.ArgumentNullException(nameof(loop)) : (new
            {
                header = loop.Header.Value,
                blocks = loop.Blocks.Select(block => block.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(block)) : (block.Value)).Order().ToImmutableArray(),
                writes = loop.Writes.Select(variable => variable.IsDefault ? throw new System.ArgumentOutOfRangeException(nameof(variable)) : (variable.Value)).ToImmutableArray(),
                tracked = LoopInvariantCandidates.State(candidate, loop, TestContext.CurrentContext.CancellationToken).Tracked.Select(printer.Print).ToImmutableArray(),
                anchors = LoopInvariantCandidates.State(candidate, loop, TestContext.CurrentContext.CancellationToken).Anchors.Select(printer.Print).ToImmutableArray()
            })).ToImmutableArray();
            var generated = LoopInvariantCandidates.Generate(candidate, loops, TestContext.CurrentContext.CancellationToken);
            observation["generated"] = generated.Select((invariant, ordinal) => invariant is null ? throw new System.ArgumentNullException(nameof(invariant)) : (new
            {
                ordinal,
                header = invariant.Header.Value,
                condition = printer.Print(invariant.Condition)
            })).ToImmutableArray();
            observation["generatedCount"] = generated.Length;
            var ordinaryBuilt = PassiveCallableVcBuilder.TryBuild(candidate, out var plan, out var failure, cancellationToken: TestContext.CurrentContext.CancellationToken);
            observation["ordinaryBuilt"] = ordinaryBuilt;
            observation["ordinaryFailure"] = failure.ToString();
            observation["finitePlanAvailable"] = plan?.LoopSearch != null;
            if (!ordinaryBuilt || plan?.LoopSearch == null || loops.Length != 1 || generated.Length != 48)
            {
                throw new AssertionException("The actual ordinary/finite plan, single loop and48generated templates are required.");
            }
            var full = PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, generated, TestContext.CurrentContext.CancellationToken);
            observation["fullBuilt"] = full != null;
            if (full != null)
            {
                throw new AssertionException("The full generated joint construction must exceed the unchanged default construction limit.");
            }
            var singletonBuilds = new List<bool>();
            observation["singletonBuilds"] = singletonBuilds;
            foreach (var invariant in generated)
            {
                singletonBuilds.Add(PassiveCallableVcBuilder.TryBuildWithInvariants(candidate, [invariant], TestContext.CurrentContext.CancellationToken) != null);
            }
            Assert.That(singletonBuilds, Has.Count.EqualTo(48));
            Assert.That(singletonBuilds.All(built => built), Is.True, "Every actual generated singleton must remain constructible.");
            observation["disposition"] = "Strict component construction separation; no normal native/Houdini execution established";
        }
        catch (Exception exception)
        {
            observation["disposition"] = "Failed: preserve actual exception and partial observation";
            observation["failureType"] = exception.GetType().FullName;
            observation["failureMessage"] = exception.Message;
            throw;
        }
        finally
        {
            await File.WriteAllTextAsync(Path.Combine(output, "observation.json"),
                JsonSerializer.Serialize(observation) + "\n", new UTF8Encoding(false), TestContext.CurrentContext.CancellationToken);
        }
    }

    private static string Hash(ReadOnlySpan<byte> /* SPINT001: all values valid - empty spans hash the empty payload */ bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes));
    }

    private static ImmutableArray<int[]> Controls()
    {
        var values = ImmutableArray.CreateBuilder<int[]>();
        for (var index = 0; index < 5; index++)
        {
            var count = index switch { 0 => 0, 1 => 1, 2 => 16, 3 => 32, _ => 64 };
            var inputs = Enumerable.Repeat(-1, 16).ToArray();
            inputs[0] = count;
            values.Add(inputs);
        }
        for (var ordinal = 1; ordinal < 16; ordinal++)
        {
            var inputs = Enumerable.Repeat(-1, 16).ToArray();
            inputs[0] = 64;
            inputs[ordinal] = -3;
            values.Add(inputs);
        }
        return values.ToImmutable();
    }

    private static uint ReferenceChecksum(int[] inputs)
    {
        System.ArgumentNullException.ThrowIfNull(inputs, nameof(inputs));
        var hash = 2166136261U;
        for (var round = 0; round < 32; round++)
        {
            foreach (var input in inputs)
            {
                hash = unchecked((hash ^ (uint)input) * 16777619U);
            }
        }
        return hash;
    }
}
