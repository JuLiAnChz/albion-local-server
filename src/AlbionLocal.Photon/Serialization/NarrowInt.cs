namespace AlbionLocal.Photon.Serialization;

/// <summary>
/// An int that <see cref="Protocol18.WriteTyped"/> writes in the narrowest form, as Photon's own serializer
/// does: 0 = IntZero, 1..255 = Int1 + byte, 256..65535 = Int2 + ushort LE (Int1_/Int2_ for the negatives),
/// anything else CompressedInt. A plain int always goes out as CompressedInt. The official server's
/// Photon-level keepalive (event 2 key 0) and the client's op 2 counter use these forms (captures: 0B nn,
/// and 0D nnnn above 255); the reader gives back an int either way.
/// </summary>
public readonly record struct NarrowInt(int Value);
