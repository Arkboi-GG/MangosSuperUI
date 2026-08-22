using System.Numerics;

namespace MangosSuperUI.Services.WeaponForge;

/// <summary>
/// Quadric-error-metric edge-collapse decimation for TEXTURED meshes (Garland–Heckbert with
/// endpoint placement). Built for the import page: a high-poly GLB that already carries UVs and a
/// texture is reduced to a game budget WITHOUT smearing the texture.
///
/// How UVs survive:
/// - Topology and quadrics operate on POSITION-WELDED vertices, but attributes live on the
///   original wedges (position+UV pairs). A collapse never invents a position or a UV — the dying
///   vertex's wedges are re-pointed at the surviving vertex's POSITION while keeping their own
///   UVs, so every surviving texel mapping is one the artist authored ("subset placement").
/// - UV seams and open boundaries are detected up front and their vertices are LOCKED: stage 1
///   only collapses interior geometry. If the target cannot be reached (heavily-seamed atlas),
///   stage 2 relaxes to seam-vertex→seam-vertex collapses, which slide along the seam instead of
///   crossing it.
/// - A collapse that would flip any surviving face's normal is rejected.
/// </summary>
public static class UvPreservingDecimator
{
    /// <summary>Collapse <paramref name="mesh"/> down to at most <paramref name="targetTriangles"/>
    /// triangles. Returns the original instance untouched when it is already within budget.</summary>
    public static RigidWeaponMesh Decimate(RigidWeaponMesh mesh, int targetTriangles, out string summary)
    {
        int wedgeCount = mesh.VertexCount;
        int faceCount = mesh.TriangleCount;
        if (faceCount <= targetTriangles || faceCount == 0)
        {
            summary = $"no decimation needed ({faceCount} ≤ {targetTriangles} triangles)";
            return mesh;
        }

        // ---- Weld wedges by position (topology space) ----
        var weldOf = new int[wedgeCount];
        var weldPos = new List<Vector3>();
        var weldLookup = new Dictionary<(int, int, int), int>();
        for (int i = 0; i < wedgeCount; i++)
        {
            var p = mesh.Positions[i];
            var key = ((int)MathF.Round(p.X * 20000f), (int)MathF.Round(p.Y * 20000f), (int)MathF.Round(p.Z * 20000f));
            if (!weldLookup.TryGetValue(key, out int w))
            {
                w = weldPos.Count;
                weldPos.Add(p);
                weldLookup[key] = w;
            }
            weldOf[i] = w;
        }
        int weldCount = weldPos.Count;

        // ---- Faces (wedge ids) + adjacency (by weld) ----
        var faces = new int[faceCount * 3];
        var faceAlive = new bool[faceCount];
        var facesOfWeld = new List<int>[weldCount];
        for (int w = 0; w < weldCount; w++) facesOfWeld[w] = new List<int>();
        int alive = 0;
        for (int f = 0; f < faceCount; f++)
        {
            int a = (int)mesh.Indices[f * 3], b = (int)mesh.Indices[f * 3 + 1], c = (int)mesh.Indices[f * 3 + 2];
            faces[f * 3] = a; faces[f * 3 + 1] = b; faces[f * 3 + 2] = c;
            int wa = weldOf[a], wb = weldOf[b], wc = weldOf[c];
            if (wa == wb || wb == wc || wa == wc) { faceAlive[f] = false; continue; } // degenerate input
            faceAlive[f] = true; alive++;
            facesOfWeld[wa].Add(f); facesOfWeld[wb].Add(f); facesOfWeld[wc].Add(f);
        }

        // ---- Connected components + per-part floors ----
        // Multi-piece exports (gems, fittings, pommels as separate shells) are where plain QEM does
        // its worst: a 54-triangle gem carries almost no geometric error, so at game budgets whole
        // parts collapse to nothing while the blade keeps triangles it does not need. Every part
        // gets a floor proportional to its share of the budget (never below a few triangles), and a
        // collapse that would take its part under the floor is refused — detail is reduced, not
        // deleted. Floors that cannot all fit the budget are scaled down but keep the minimum, so
        // a tiny budget yields low-poly blobs where the gems were rather than holes.
        var compOf = new int[weldCount];
        int compCount;
        {
            var parent = new int[weldCount];
            for (int w = 0; w < weldCount; w++) parent[w] = w;
            int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int x, int y) { x = Find(x); y = Find(y); if (x != y) parent[x] = y; }
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                int wa = weldOf[faces[f * 3]], wb = weldOf[faces[f * 3 + 1]], wc = weldOf[faces[f * 3 + 2]];
                Union(wa, wb); Union(wb, wc);
            }
            var ids = new Dictionary<int, int>();
            for (int w = 0; w < weldCount; w++)
            {
                int root = Find(w);
                if (!ids.TryGetValue(root, out int id)) { id = ids.Count; ids[root] = id; }
                compOf[w] = id;
            }
            compCount = ids.Count;
        }
        var compAlive = new int[compCount];
        for (int f = 0; f < faceCount; f++)
            if (faceAlive[f]) compAlive[compOf[weldOf[faces[f * 3]]]]++;
        const int MinPartFloor = 4;
        var compFloor = new int[compCount];
        {
            double ratio = (double)targetTriangles / Math.Max(alive, 1);
            long floorSum = 0;
            for (int c = 0; c < compCount; c++)
            {
                compFloor[c] = Math.Max(MinPartFloor, (int)Math.Round(compAlive[c] * ratio));
                compFloor[c] = Math.Min(compFloor[c], compAlive[c]);
                floorSum += compFloor[c];
            }
            // If the proportional floors cannot all fit (many tiny parts at a small budget), scale
            // them down toward the minimum so the budget remains reachable where possible.
            if (floorSum > targetTriangles && compCount > 1)
            {
                double scale = (double)targetTriangles / floorSum;
                for (int c = 0; c < compCount; c++)
                    compFloor[c] = Math.Min(compAlive[c], Math.Max(MinPartFloor, (int)Math.Floor(compFloor[c] * scale)));
            }
        }

        // ---- Lock seams and boundaries ----
        // Seam vertex: its wedges carry more than one distinct UV. Boundary vertex: on an edge with
        // only one adjacent face. Both must survive stage 1 so charts keep their outlines.
        var locked = new bool[weldCount];
        {
            var uvSeen = new List<Vector2>[weldCount];
            for (int i = 0; i < wedgeCount; i++)
            {
                int w = weldOf[i];
                var uv = mesh.Uv0[i];
                uvSeen[w] ??= new List<Vector2>(2);
                bool known = false;
                foreach (var u in uvSeen[w])
                    if (MathF.Abs(u.X - uv.X) < 1e-4f && MathF.Abs(u.Y - uv.Y) < 1e-4f) { known = true; break; }
                if (!known)
                {
                    uvSeen[w].Add(uv);
                    if (uvSeen[w].Count > 1) locked[w] = true;
                }
            }
            var edgeUse = new Dictionary<(int, int), int>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int wa = weldOf[faces[f * 3 + k]], wb = weldOf[faces[f * 3 + (k + 1) % 3]];
                    var e = wa < wb ? (wa, wb) : (wb, wa);
                    edgeUse[e] = edgeUse.TryGetValue(e, out int cnt) ? cnt + 1 : 1;
                }
            }
            foreach (var (e, cnt) in edgeUse)
                if (cnt == 1) { locked[e.Item1] = true; locked[e.Item2] = true; }
        }

        // ---- Per-weld quadrics (area-weighted face planes) ----
        var q = new double[weldCount][];
        for (int w = 0; w < weldCount; w++) q[w] = new double[10];
        for (int f = 0; f < faceCount; f++)
        {
            if (!faceAlive[f]) continue;
            var (n, area) = FaceNormalArea(f);
            if (area < 1e-12f) continue;
            float d = -Vector3.Dot(n, weldPos[weldOf[faces[f * 3]]]);
            AddPlane(q[weldOf[faces[f * 3]]], n, d, area);
            AddPlane(q[weldOf[faces[f * 3 + 1]]], n, d, area);
            AddPlane(q[weldOf[faces[f * 3 + 2]]], n, d, area);
        }

        // ---- Feature edges: the model's SILHOUETTE ----
        // Plain QEM eats large flat regions first (a blade collapses at ~zero error while an ornate
        // guard resists), so at game-budget ratios the biggest feature of a weapon vanishes. Sharp
        // edges (dihedral above the threshold) and open borders are the silhouette: their vertices
        // are locked like seams (they may only slide along other locked vertices), and each feature
        // edge adds a strong perpendicular constraint quadric so the outline coarsens along itself
        // instead of eroding inward (Garland–Heckbert boundary constraint).
        const float FeatureCos = 0.5f;        // faces disagreeing by more than ~60° form a feature edge
        const float FeaturePenalty = 100f;    // constraint weight, scaled by edge length²
        int featureEdges = 0;
        {
            var edgeFaces = new Dictionary<(int, int), (int F0, int F1, int Count)>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int wa = weldOf[faces[f * 3 + k]], wb = weldOf[faces[f * 3 + (k + 1) % 3]];
                    var e = wa < wb ? (wa, wb) : (wb, wa);
                    if (edgeFaces.TryGetValue(e, out var rec)) edgeFaces[e] = (rec.F0, f, rec.Count + 1);
                    else edgeFaces[e] = (f, -1, 1);
                }
            }
            foreach (var (e, rec) in edgeFaces)
            {
                bool feature;
                if (rec.Count == 1) feature = true; // open border
                else if (rec.Count == 2)
                {
                    var (n0, a0) = FaceNormalArea(rec.F0);
                    var (n1, a1) = FaceNormalArea(rec.F1);
                    feature = a0 > 1e-12f && a1 > 1e-12f && Vector3.Dot(n0, n1) < FeatureCos;
                }
                else feature = true; // non-manifold junction — treat as silhouette
                if (!feature) continue;

                featureEdges++;
                locked[e.Item1] = true;
                locked[e.Item2] = true;

                // Constraint plane per adjacent face: contains the edge, perpendicular to the face.
                var pa = weldPos[e.Item1];
                var pb = weldPos[e.Item2];
                var edgeDir = pb - pa;
                float len2 = edgeDir.LengthSquared();
                if (len2 < 1e-14f) continue;
                foreach (int f in new[] { rec.F0, rec.F1 })
                {
                    if (f < 0) continue;
                    var (fn, fa) = FaceNormalArea(f);
                    if (fa < 1e-12f) continue;
                    var cn = Vector3.Cross(edgeDir, fn);
                    float cl = cn.Length();
                    if (cl < 1e-9f) continue;
                    cn /= cl;
                    float cd = -Vector3.Dot(cn, pa);
                    AddPlane(q[e.Item1], cn, cd, FeaturePenalty * len2);
                    AddPlane(q[e.Item2], cn, cd, FeaturePenalty * len2);
                }
            }
        }

        // ---- Collapse loop (stage 1: interior only; stage 2: seam-along-seam) ----
        var weldAliveArr = new bool[weldCount];
        Array.Fill(weldAliveArr, true);
        var version = new int[weldCount];
        var heap = new PriorityQueue<(int A, int B, int Va, int Vb), double>();
        int flipRejected = 0, floorRejected = 0;
        bool relaxed = false;

        void PushEdgesOf(int w, bool relax)
        {
            foreach (int f in facesOfWeld[w])
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int o = weldOf[faces[f * 3 + k]];
                    if (o == w) continue;
                    PushCandidate(w, o, relax);
                    PushCandidate(o, w, relax);
                }
            }
        }
        void PushCandidate(int a, int b, bool relax)
        {
            if (!weldAliveArr[a] || !weldAliveArr[b]) return;
            if (locked[a] && !relax) return;              // stage 1: seams/boundaries survive
            if (locked[a] && relax && !locked[b]) return; // stage 2: seams may only slide along seams
            heap.Enqueue((a, b, version[a], version[b]), Cost(a, b));
        }
        double Cost(int a, int b)
        {
            var p = weldPos[b]; // endpoint placement: the survivor keeps its authored position/UV
            return Eval(q[a], p.X, p.Y, p.Z) + Eval(q[b], p.X, p.Y, p.Z);
        }

        void SeedHeap(bool relax)
        {
            var seen = new HashSet<(int, int)>();
            for (int f = 0; f < faceCount; f++)
            {
                if (!faceAlive[f]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int wa = weldOf[faces[f * 3 + k]], wb = weldOf[faces[f * 3 + (k + 1) % 3]];
                    if (seen.Add((wa, wb))) PushCandidate(wa, wb, relax);
                    if (seen.Add((wb, wa))) PushCandidate(wb, wa, relax);
                }
            }
        }
        SeedHeap(false);

        while (alive > targetTriangles)
        {
            if (heap.Count == 0)
            {
                if (relaxed) break; // even seam-sliding exhausted — give what we have
                relaxed = true;
                SeedHeap(true);
                if (heap.Count == 0) break;
                continue;
            }
            var (a, b, va, vb) = heap.Dequeue();
            if (!weldAliveArr[a] || !weldAliveArr[b]) continue;
            if (version[a] != va || version[b] != vb)
            {
                if (StillNeighbours(a, b)) PushCandidate(a, b, relaxed);
                continue;
            }
            if (!StillNeighbours(a, b)) continue;

            // Reject collapses that flip a surviving face.
            bool flips = false;
            foreach (int f in facesOfWeld[a])
            {
                if (!faceAlive[f] || FaceUsesWeld(f, b)) continue;
                if (NormalFlips(f, a, weldPos[b])) { flips = true; break; }
            }
            if (flips) { flipRejected++; continue; }

            // ---- Execute a → b ----
            // Decide kill-vs-move for EVERY face of a BEFORE mutating the wedge→weld mapping:
            // wedges are shared between faces, so remapping one face's wedge makes later faces
            // spuriously "contain b" and killing them blows holes in the fan around the collapse.
            var toKill = new List<int>();
            var toMove = new List<int>();
            foreach (int f in facesOfWeld[a])
            {
                if (!faceAlive[f]) continue;
                if (FaceUsesWeld(f, b)) toKill.Add(f); else toMove.Add(f);
            }
            // Part floor: never take a connected piece (gem, pommel, fitting) below its share.
            int comp = compOf[a];
            if (compAlive[comp] - toKill.Count < compFloor[comp]) { floorRejected++; continue; }

            weldAliveArr[a] = false;
            version[a]++; version[b]++;
            foreach (int f in toKill) { faceAlive[f] = false; alive--; compAlive[comp]--; }
            foreach (int f in toMove)
            {
                for (int k = 0; k < 3; k++)
                {
                    int wedge = faces[f * 3 + k];
                    if (weldOf[wedge] == a) weldOf[wedge] = b;
                }
                facesOfWeld[b].Add(f);
            }
            facesOfWeld[a] = new List<int>();
            var qb2 = q[b];
            var qa2 = q[a];
            for (int i = 0; i < 10; i++) qb2[i] += qa2[i];
            locked[b] |= locked[a];
            PushEdgesOf(b, relaxed);
        }

        // ---- Rebuild a compact RigidWeaponMesh (wedges keep their own UVs) ----
        var remap = new Dictionary<int, int>();
        var newPos = new List<Vector3>();
        var newUv = new List<Vector2>();
        var newNrmAcc = new List<Vector3>();
        var newIdx = new List<uint>();
        var newRegions = mesh.TriangleRegionIds is null ? null : new List<string>();
        for (int f = 0; f < faceCount; f++)
        {
            if (!faceAlive[f]) continue;
            var (fn, _) = FaceNormalArea(f);
            for (int k = 0; k < 3; k++)
            {
                int wedge = faces[f * 3 + k];
                if (!remap.TryGetValue(wedge, out int ni))
                {
                    ni = newPos.Count;
                    remap[wedge] = ni;
                    newPos.Add(weldPos[weldOf[wedge]]);
                    newUv.Add(mesh.Uv0[wedge]);
                    newNrmAcc.Add(Vector3.Zero);
                }
                newNrmAcc[ni] += fn;
                newIdx.Add((uint)ni);
            }
            newRegions?.Add(mesh.TriangleRegionIds![f]);
        }
        var newNrm = new Vector3[newNrmAcc.Count];
        for (int i = 0; i < newNrm.Length; i++)
        {
            var n = newNrmAcc[i];
            newNrm[i] = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.UnitY;
        }

        int partsAtFloor = 0;
        for (int c = 0; c < compCount; c++) if (compAlive[c] > 0 && compAlive[c] <= compFloor[c]) partsAtFloor++;
        summary = $"decimated {faceCount:N0} → {alive:N0} triangles " +
                  $"({featureEdges:N0} silhouette edges protected, {(relaxed ? "silhouette-slide" : "interior-only")}, " +
                  $"{flipRejected} flip-rejected, {compCount} part{(compCount == 1 ? "" : "s")} kept" +
                  $"{(partsAtFloor > 0 ? $" — {partsAtFloor} at their detail floor, {floorRejected} collapses refused to keep them" : "")}" +
                  $"{(alive > targetTriangles ? $"; stopped above the {targetTriangles:N0} target because every part is at its floor — raise the budget for more detail" : "")}, UVs preserved)";
        return new RigidWeaponMesh
        {
            Positions = newPos.ToArray(),
            Normals = newNrm,
            Uv0 = newUv.ToArray(),
            Indices = newIdx.ToArray(),
            VertexIds = null,
            Material = mesh.Material,
            TriangleRegionIds = newRegions?.ToArray(),
            Normalization = new MeshNormalizationRecord
            {
                Scale = mesh.Normalization.Scale,
                Translation = mesh.Normalization.Translation,
                WindingReversed = mesh.Normalization.WindingReversed,
                Method = mesh.Normalization.Method + $" + qem-decimate({targetTriangles})",
            },
        };

        // ---- local helpers ----
        (Vector3 N, float Area) FaceNormalArea(int f)
        {
            var p0 = weldPos[weldOf[faces[f * 3]]];
            var p1 = weldPos[weldOf[faces[f * 3 + 1]]];
            var p2 = weldPos[weldOf[faces[f * 3 + 2]]];
            var cross = Vector3.Cross(p1 - p0, p2 - p0);
            float len = cross.Length();
            return len < 1e-12f ? (Vector3.UnitY, 0f) : (cross / len, len * 0.5f);
        }
        bool FaceUsesWeld(int f, int w)
        {
            return weldOf[faces[f * 3]] == w || weldOf[faces[f * 3 + 1]] == w || weldOf[faces[f * 3 + 2]] == w;
        }
        bool StillNeighbours(int a, int b)
        {
            foreach (int f in facesOfWeld[a])
                if (faceAlive[f] && FaceUsesWeld(f, b)) return true;
            return false;
        }
        bool NormalFlips(int f, int movingWeld, Vector3 to)
        {
            var p = new Vector3[3];
            var p2 = new Vector3[3];
            for (int k = 0; k < 3; k++)
            {
                int w = weldOf[faces[f * 3 + k]];
                p[k] = weldPos[w];
                p2[k] = w == movingWeld ? to : weldPos[w];
            }
            var n0 = Vector3.Cross(p[1] - p[0], p[2] - p[0]);
            var n1 = Vector3.Cross(p2[1] - p2[0], p2[2] - p2[0]);
            return Vector3.Dot(n0, n1) <= 1e-12f;
        }
    }

    /// <summary>Accumulate the fundamental quadric of plane (n, d), weighted by face area.</summary>
    private static void AddPlane(double[] q, Vector3 n, float d, float area)
    {
        double a = n.X, b = n.Y, c = n.Z, dd = d, w = area;
        q[0] += w * a * a; q[1] += w * a * b; q[2] += w * a * c; q[3] += w * a * dd;
        q[4] += w * b * b; q[5] += w * b * c; q[6] += w * b * dd;
        q[7] += w * c * c; q[8] += w * c * dd;
        q[9] += w * dd * dd;
    }

    /// <summary>v^T Q v for v = (x, y, z, 1).</summary>
    private static double Eval(double[] q, double x, double y, double z) =>
        q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x +
        q[4] * y * y + 2 * q[5] * y * z + 2 * q[6] * y +
        q[7] * z * z + 2 * q[8] * z +
        q[9];
}
