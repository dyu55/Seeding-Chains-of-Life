using System.Collections.Generic;
using UnityEngine;

namespace SCoL.Voxels
{
    /// <summary>
    /// Minimal voxel surface mesher (faces for non-air blocks where neighbor is air).
    /// Not greedy meshing (yet) — good enough to validate gameplay + VR.
    /// </summary>
    public static class VoxelMesher
    {
        private struct Face
        {
            public Vector3 a, b, c, d;
            public Vector3 normal;
        }

        private static readonly Face[] Faces =
        {
            // +X
            new Face{ a=new(1,0,0), b=new(1,0,1), c=new(1,1,1), d=new(1,1,0), normal=Vector3.right },
            // -X
            new Face{ a=new(0,0,1), b=new(0,0,0), c=new(0,1,0), d=new(0,1,1), normal=Vector3.left },
            // +Y
            new Face{ a=new(0,1,0), b=new(1,1,0), c=new(1,1,1), d=new(0,1,1), normal=Vector3.up },
            // -Y
            new Face{ a=new(0,0,1), b=new(1,0,1), c=new(1,0,0), d=new(0,0,0), normal=Vector3.down },
            // +Z
            new Face{ a=new(1,0,1), b=new(0,0,1), c=new(0,1,1), d=new(1,1,1), normal=Vector3.forward },
            // -Z
            new Face{ a=new(0,0,0), b=new(1,0,0), c=new(1,1,0), d=new(0,1,0), normal=Vector3.back },
        };

        private static readonly Vector2[] QuadUV =
        {
            new(0,0), new(1,0), new(1,1), new(0,1)
        };

        public static Mesh BuildChunkMesh(VoxelWorld world, Vector2Int chunkCoord)
        {
            var cfg = world.Config;
            int cs = cfg.chunkSize;
            int h = cfg.worldHeight;

            var verts = new List<Vector3>(cs * cs * 24);
            var norms = new List<Vector3>(cs * cs * 24);
            var uvs = new List<Vector2>(cs * cs * 24);

            // Triangles per block type (submeshes)
            var tris = new Dictionary<VoxelBlockType, List<int>>();

            // local-to-world offset of this chunk
            int baseX = chunkCoord.x * cs;
            int baseZ = chunkCoord.y * cs;

            for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            for (int y = 0; y < h; y++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;

                var t = world.GetBlock(wx, y, wz);
                if (t == VoxelBlockType.Air) continue;

                // For each face, emit if neighbor is air
                for (int f = 0; f < 6; f++)
                {
                    int nx = wx, ny = y, nz = wz;
                    switch (f)
                    {
                        case 0: nx = wx + 1; break;
                        case 1: nx = wx - 1; break;
                        case 2: ny = y + 1; break;
                        case 3: ny = y - 1; break;
                        case 4: nz = wz + 1; break;
                        case 5: nz = wz - 1; break;
                    }

                    var nt = world.GetBlock(nx, ny, nz);
                    if (nt != VoxelBlockType.Air) continue;

                    if (!tris.TryGetValue(t, out var tlist))
                    {
                        tlist = new List<int>(1024);
                        tris[t] = tlist;
                    }

                    int vi = verts.Count;
                    var face = Faces[f];

                    // Quad vertices (clockwise)
                    verts.Add(new Vector3(wx, y, wz) + face.a);
                    verts.Add(new Vector3(wx, y, wz) + face.b);
                    verts.Add(new Vector3(wx, y, wz) + face.c);
                    verts.Add(new Vector3(wx, y, wz) + face.d);

                    norms.Add(face.normal);
                    norms.Add(face.normal);
                    norms.Add(face.normal);
                    norms.Add(face.normal);

                    uvs.Add(QuadUV[0]);
                    uvs.Add(QuadUV[1]);
                    uvs.Add(QuadUV[2]);
                    uvs.Add(QuadUV[3]);

                    // two triangles
                    tlist.Add(vi + 0);
                    tlist.Add(vi + 2);
                    tlist.Add(vi + 1);

                    tlist.Add(vi + 0);
                    tlist.Add(vi + 3);
                    tlist.Add(vi + 2);
                }
            }

            var mesh = new Mesh
            {
                indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16
            };
            mesh.SetVertices(verts);
            mesh.SetNormals(norms);
            mesh.SetUVs(0, uvs);

            // stable material order
            var types = new List<VoxelBlockType>(tris.Keys);
            types.Sort();

            mesh.subMeshCount = types.Count;
            for (int i = 0; i < types.Count; i++)
                mesh.SetTriangles(tris[types[i]], i, true);

            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
