using System;
using System.IO;

namespace PlaqueRamen;

internal readonly struct MachineButton
{
    internal readonly float X, Y, Z;
    internal MachineButton(float x, float y, float z) { X = x; Y = y; Z = z; }
}

internal static class MachineLayout
{
    // The reviewed machine faces local +X. From its front, +Z is right.
    // Confirm the physical anchors as well as the count before using native IDs.
    internal static void Validate(MachineButton[] buttons)
    {
        if (buttons.Length != 9) throw new InvalidDataException("Expected a three-by-three ticket machine.");
        for (int i = 0; i < 9; i++)
        {
            var p = buttons[i];
            if (float.IsNaN(p.X + p.Y + p.Z) || float.IsInfinity(p.X + p.Y + p.Z)
                || Math.Abs(p.X - buttons[0].X) > 0.04f
                || Math.Abs(p.Y - buttons[i / 3 * 3].Y) > 0.02f
                || Math.Abs(p.Z - buttons[i % 3].Z) > 0.02f
                || i % 3 != 0 && p.Z - buttons[i - 1].Z < 0.04f
                || i >= 3 && buttons[i - 3].Y - p.Y < 0.04f)
                throw new InvalidDataException("Ticket-machine button order changed; expected rows from top left to bottom right.");
        }
    }
}
