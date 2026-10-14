"""
bundle_to_hskin.py <asset bundle> <mesh name> <out.hskin>

Reads a skinned mesh out of a compiled Unity asset bundle (e.g. TabardShare's "tabardshare") with UnityPy
(pip install UnityPy) and writes it as a small binary the mod builds at runtime, so no bundle has to be
loaded (a bundle can't be loaded twice, which would clash with TabardShare itself).
The data is already in Unity's space, so nothing is converted; bind poses are kept exactly.

Format (little endian): "HSKN", int version=1,
  int vertexCount, float3 positions, float3 normals, float2 uv,
  int indexCount, int indices,
  per vertex: int4 bone indices, float4 bone weights,
  int boneCount, per bone: string name (int length + UTF-8), float[16] bind pose (column major, as Unity's Matrix4x4),
  int blendShapeCount, per shape: string name, float3 position deltas, float3 normal deltas (one frame, weight 100).
"""
import sys, struct
import UnityPy
from UnityPy.helpers.MeshHelper import MeshHandler

def s(f, text):
    b = text.encode('utf-8'); f.write(struct.pack('<i', len(b))); f.write(b)

def main(bundle, mesh_name, out):
    env = UnityPy.load(bundle)
    mesh = renderer = None
    for o in env.objects:
        if o.type.name == 'Mesh':
            m = o.read()
            if m.m_Name == mesh_name: mesh = m
        elif o.type.name == 'SkinnedMeshRenderer':
            renderer = o.read()
    if mesh is None: raise SystemExit(f"no mesh '{mesh_name}'")
    h = MeshHandler(mesh); h.process()
    n = h.m_VertexCount
    bones = []
    for p in renderer.m_Bones:
        t = p.read(); go = t.m_GameObject.read(); bones.append(go.m_Name)
    with open(out, 'wb') as f:
        f.write(b'HSKN'); f.write(struct.pack('<ii', 1, n))
        for v in h.m_Vertices: f.write(struct.pack('<3f', *v[:3]))
        for v in h.m_Normals: f.write(struct.pack('<3f', *v[:3]))
        for v in h.m_UV0: f.write(struct.pack('<2f', *v[:2]))
        idx = list(h.m_IndexBuffer)
        f.write(struct.pack('<i', len(idx))); f.write(struct.pack(f'<{len(idx)}i', *idx))
        for i in range(n):
            f.write(struct.pack('<4i', *[int(x) for x in h.m_BoneIndices[i][:4]]))
            f.write(struct.pack('<4f', *h.m_BoneWeights[i][:4]))
        f.write(struct.pack('<i', len(bones)))
        for name, bp in zip(bones, mesh.m_BindPose):
            s(f, name)
            # UnityPy's Matrix4x4f: e00 e10 e20 e30 e01 ... (column major, like Unity's own serialisation)
            vals = [getattr(bp, f'e{r}{c}') for c in range(4) for r in range(4)]
            f.write(struct.pack('<16f', *vals))
        shapes = mesh.m_Shapes
        f.write(struct.pack('<i', len(shapes.channels)))
        for ch in shapes.channels:
            s(f, ch.name)
            shape = shapes.shapes[ch.frameIndex]
            dpos = [(0.0, 0.0, 0.0)] * n; dnrm = [(0.0, 0.0, 0.0)] * n
            for sv in shapes.vertices[shape.firstVertex:shape.firstVertex + shape.vertexCount]:
                dpos[sv.index] = (sv.vertex.x, sv.vertex.y, sv.vertex.z)
                dnrm[sv.index] = (sv.normal.x, sv.normal.y, sv.normal.z)
            for d in dpos: f.write(struct.pack('<3f', *d))
            for d in dnrm: f.write(struct.pack('<3f', *d))
    print(f"{out}: {n} vertices, {len(idx)//3} triangles, {len(bones)} bones, shapes {[c.name for c in shapes.channels]}")

if __name__ == '__main__':
    main(*sys.argv[1:4])
