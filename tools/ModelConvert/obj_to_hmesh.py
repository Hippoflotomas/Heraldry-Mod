"""
obj_to_hmesh.py <model.obj> <out.hmesh> [--center] [--only <object>[,<object>...]] [--face-as-is]

Turns a model into the small binary mesh the mod builds at runtime (no Unity asset bundles).
Make the .obj with assimp, which bakes Blender's object transforms in:
    assimp export "Herald Table2.fbx" table.obj

- --only keeps just the named objects (as assimp names them in the .obj, e.g. "Cylinder.001"), for a file
  that holds a whole scene (PaintBottle1.fbx also has the table in it).
- --face-as-is keeps the ValheimHeraldryMod_Face faces as they are (submesh named after the material, not lifted,
  UVs untouched): for models whose tagged face is found at runtime like another modder's (the StandardLarge
  banner's cloth), instead of becoming the table-style "Display" part.
- One submesh per material slot (Wood, Metal, ...); the mod puts a vanilla material on each by name.
- Converts Blender/assimp space (right-handed, centimetres) to Unity's (left-handed, metres): x is negated,
  triangle winding flipped, everything scaled by 0.01.
- Faces with the material "ValheimHeraldryMod_Face" are the paintable face: they become the submesh "Display",
  lifted 3 mm, with fresh flat 0..1 UVs (the modeller's UVs there are ignored), top of the image towards the
  face's highest edge. Make it in Blender by duplicating the face polygons into their own object with only
  that material.

Format (little endian): "HMSH", int version=1, int submeshes, then per submesh:
  string name (int length + UTF-8), int vertexCount, float3 positions, float3 normals, float2 uvs,
  int indexCount, int indices.
"""
import sys, struct, math
import numpy as np

def read_obj(path):
    V, VT, VN = [], [], []
    parts = {}  # (group, material) -> list of (v, vt, vn) corners, three per triangle
    g, m = None, None
    for line in open(path):
        p = line.split()
        if not p: continue
        if p[0] == 'v': V.append([float(x) for x in p[1:4]])
        elif p[0] == 'vt': VT.append([float(x) for x in p[1:3]])
        elif p[0] == 'vn': VN.append([float(x) for x in p[1:4]])
        elif p[0] == 'g': g = p[1]
        elif p[0] == 'usemtl': m = p[1]
        elif p[0] == 'f':
            corners = []
            for c in p[1:]:
                ids = (c.split('/') + ['', ''])[:3]
                corners.append(tuple(int(x) - 1 if x else -1 for x in ids))
            for k in range(1, len(corners) - 1):  # fan-triangulate
                parts.setdefault((g, m), []).extend([corners[0], corners[k], corners[k + 1]])
    return np.array(V), np.array(VT) if VT else np.zeros((1, 2)), np.array(VN) if VN else np.zeros((1, 3)), parts

def to_unity(p):  # right-handed cm -> left-handed m
    q = np.array(p, dtype=float) * 0.01
    q[..., 0] *= -1
    return q

def build_submeshes(V, VT, VN, parts):
    by_mat = {}
    for (g, m), corners in parts.items():
        by_mat.setdefault(m, []).extend(corners)
    out = []
    for mat, corners in sorted(by_mat.items()):
        index_of, pos, nrm, uv, idx = {}, [], [], [], []
        for c in corners:
            if c not in index_of:
                index_of[c] = len(pos)
                pos.append(to_unity(V[c[0]]))
                n = VN[c[2]] if c[2] >= 0 else [0, 1, 0]
                n = np.array(n, dtype=float); n[0] *= -1
                nrm.append(n / (np.linalg.norm(n) or 1))
                uv.append(VT[c[1]] if c[1] >= 0 else [0, 0])
            idx.append(index_of[c])
        idx = np.array(idx).reshape(-1, 3)[:, [0, 2, 1]].reshape(-1)  # flip winding for the handedness change
        out.append((mat, np.array(pos), np.array(nrm), np.array(uv), idx))
    return out

FACE_MATERIAL = 'ValheimHeraldryMod_Face'

def face_display(sub, lift=0.003):
    """The modeller's paintable face (material ValheimHeraldryMod_Face) becomes the "Display" submesh: lifted a
    little off what's under it, with fresh flat 0..1 UVs (same scale both ways, centred), the top of the image
    towards the face's highest edge. Its triangles are also the outline the arms are painted inside."""
    _, pos, nrm, _, idx = sub
    centre = pos.mean(0)
    _, _, vt = np.linalg.svd(pos - centre)
    normal = vt[2]
    if np.dot(normal, nrm.mean(0)) < 0: normal = -normal       # the side the modeller's normals face
    up_world = np.array([0, 1, 0.0])
    up = up_world - np.dot(up_world, normal) * normal
    if np.linalg.norm(up) < 0.05: up = np.array([0, 0, 1.0]) - normal[2] * normal  # lying flat: top = +z
    up /= np.linalg.norm(up)
    right = np.cross(normal, up)                                 # as seen looking at the face
    d = pos - centre
    u, v = d @ right, d @ up
    half = max(np.abs(u).max(), np.abs(v).max())
    uv = np.stack([0.5 + 0.5 * u / half, 0.5 + 0.5 * v / half], 1)
    flat = np.abs(d @ normal).max()
    print(f"face: {len(idx) // 3} triangles, {2 * half:.3f} m across, flat to {flat * 1000:.1f} mm, normal {normal.round(3)}")
    return ('Display', pos + normal * lift, nrm, uv, idx)

def write(path, subs):
    with open(path, 'wb') as f:
        f.write(b'HMSH'); f.write(struct.pack('<ii', 1, len(subs)))
        for name, pos, nrm, uv, idx in subs:
            nb = name.encode('utf-8'); f.write(struct.pack('<i', len(nb))); f.write(nb)
            f.write(struct.pack('<i', len(pos)))
            f.write(np.asarray(pos, '<f4').tobytes()); f.write(np.asarray(nrm, '<f4').tobytes()); f.write(np.asarray(uv, '<f4').tobytes())
            f.write(struct.pack('<i', len(idx))); f.write(np.asarray(idx, '<i4').tobytes())

if __name__ == '__main__':
    src, dst = sys.argv[1], sys.argv[2]
    V, VT, VN, parts = read_obj(src)
    if '--only' in sys.argv:
        keep = set(sys.argv[sys.argv.index('--only') + 1].split(','))
        parts = {k: v for k, v in parts.items() if k[0] in keep}
        if not parts: sys.exit(f"none of {sorted(keep)} found")
    as_is = '--face-as-is' in sys.argv
    subs = [face_display(sb) if sb[0] == FACE_MATERIAL and not as_is else sb for sb in build_submeshes(V, VT, VN, parts)]
    if '--center' in sys.argv:
        # Origin at the middle of the footprint, on the floor, so the piece rotates about its centre when placed
        allp = np.vstack([s[1] for s in subs]); c = (allp.min(0) + allp.max(0)) / 2; c[1] = allp.min(0)[1]
        subs = [(n, p - c, nr, uv, ix) for n, p, nr, uv, ix in subs]
    write(dst, subs)
    allp = np.vstack([s[1] for s in subs])
    print(f"{dst}: {len(subs)} submeshes {[s[0] for s in subs]}, {sum(len(s[1]) for s in subs)} vertices, "
          f"bounds {allp.min(0).round(3)} .. {allp.max(0).round(3)} m")
