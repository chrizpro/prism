using System;
using System.Collections.Generic;
using System.Linq;
using Prism.Kernel;
using Prism.Meshing;

namespace Prism.Core.Cam;

/// <summary>Bounded, sampled geometry screening. This is not an exposure or support generator.</summary>
public sealed record ResinReport(double Cell, int Layers, int EnclosedVoids, double TrappedVolume,
    int CupLayers, double MaxCupArea, int IslandRegions, double PeakLayerArea, Vec3[] VoidPoints, Vec3[] IslandPoints);

public static class ResinAnalysis
{
    public static ResinReport Analyze(TriMesh mesh, double requestedCell)
    {
        if (!double.IsFinite(requestedCell) || requestedCell <= 0) throw new ArgumentOutOfRangeException(nameof(requestedCell));
        if (!MeshAnalysis.Report(mesh).Printable) throw new InvalidOperationException("Repair the mesh before cavity analysis: it must be closed, manifold and outward oriented.");
        double cell = Math.Max(requestedCell, Math.Max(mesh.Size.X, Math.Max(mesh.Size.Y, mesh.Size.Z)) / 80);
        var origin = mesh.Min - new Vec3(cell, cell, cell);
        int nx = (int)Math.Ceiling(mesh.Size.X / cell) + 2, ny = (int)Math.Ceiling(mesh.Size.Y / cell) + 2, nz = (int)Math.Ceiling(mesh.Size.Z / cell) + 2;
        int plane = nx * ny, count = plane * nz;
        var solid = new bool[count];
        Vec3 Point(int x, int y, int z) => origin + new Vec3((x + .5) * cell, (y + .5) * cell, (z + .5) * cell);
        // Intersections of vertical columns with the mesh. Merge shared-edge hits before parity classification.
        var columns = new List<double>?[plane];
        for (int t = 0; t < mesh.TriCount; t++)
        {
            var (ia, ib, ic) = mesh.Tri(t); var a = mesh.P(ia); var b = mesh.P(ib); var c = mesh.P(ic);
            double det = (b.Y-c.Y)*(a.X-c.X)+(c.X-b.X)*(a.Y-c.Y);
            if (Math.Abs(det) < 1e-12) continue;
            int x0 = Math.Max(0, (int)Math.Floor((Math.Min(a.X,Math.Min(b.X,c.X))-origin.X)/cell-.5));
            int x1 = Math.Min(nx-1, (int)Math.Ceiling((Math.Max(a.X,Math.Max(b.X,c.X))-origin.X)/cell-.5));
            int y0 = Math.Max(0, (int)Math.Floor((Math.Min(a.Y,Math.Min(b.Y,c.Y))-origin.Y)/cell-.5));
            int y1 = Math.Min(ny-1, (int)Math.Ceiling((Math.Max(a.Y,Math.Max(b.Y,c.Y))-origin.Y)/cell-.5));
            for(int y=y0;y<=y1;y++) for(int x=x0;x<=x1;x++)
            {
                var p=Point(x,y,0);
                double u=((b.Y-c.Y)*(p.X-c.X)+(c.X-b.X)*(p.Y-c.Y))/det;
                double v=((c.Y-a.Y)*(p.X-c.X)+(a.X-c.X)*(p.Y-c.Y))/det;
                if(u < -1e-9 || v < -1e-9 || u+v > 1+1e-9) continue;
                (columns[y*nx+x]??=[]).Add(u*a.Z+v*b.Z+(1-u-v)*c.Z);
            }
        }
        for(int i=0;i<plane;i++) if(columns[i] is {} hits)
        {
            hits.Sort(); var unique=new List<double>();
            foreach(double h in hits) if(unique.Count==0 || h-unique[^1]>cell*1e-6) unique.Add(h);
            if(unique.Count%2!=0) throw new InvalidOperationException("Ambiguous mesh intersections. Change the sampling size or repair intersecting shells.");
            int k=0;
            for(int z=0;z<nz;z++) { double pz=origin.Z+(z+.5)*cell; while(k<unique.Count && unique[k]<pz) k++; solid[z*plane+i]=k%2==1; }
        }
        if(!solid.Any(v=>v))throw new InvalidOperationException("No material resolved at this sampling size. Choose a finer grid.");
        IEnumerable<int> Neighbors(int i)
        {
            int x=i%nx,y=i/nx%ny,z=i/plane;
            if(x>0)yield return i-1;if(x+1<nx)yield return i+1;
            if(y>0)yield return i-nx;if(y+1<ny)yield return i+nx;
            if(z>0)yield return i-plane;if(z+1<nz)yield return i+plane;
        }
        // Full 3D flood separates sealed voids from channels connected to any exterior surface.
        var seen=new bool[count];var queue=new Queue<int>();queue.Enqueue(0);seen[0]=true;
        while(queue.TryDequeue(out int i))foreach(int n in Neighbors(i))if(!seen[n]&&!solid[n]){seen[n]=true;queue.Enqueue(n);}
        int voids=0,voidCells=0;var voidPoints=new List<Vec3>();
        for(int i=0;i<count;i++)if(!solid[i]&&!seen[i])
        {
            voids++;voidPoints.Add(Point(i%nx,i/nx%ny,i/plane));seen[i]=true;queue.Enqueue(i);
            while(queue.TryDequeue(out int j)){voidCells++;foreach(int n in Neighbors(j))if(!seen[n]&&!solid[n]){seen[n]=true;queue.Enqueue(n);}}
        }
        // Incremental empty-space connectivity to the sides/bottom of the already printed volume.
        // A cavity which vents only in a future layer remains a potential cup until that vent appears.
        var parent=Enumerable.Repeat(-1,count+1).ToArray();int outside=count;parent[outside]=outside;
        int Root(int i){while(parent[i]!=i){parent[i]=parent[parent[i]];i=parent[i];}return i;}
        void Join(int a,int b){a=Root(a);b=Root(b);if(a!=b)parent[a]=b;}
        int cupLayers=0,islands=0;double maxCup=0,peak=0;var islandPoints=new List<Vec3>();var visited=new bool[plane];
        for(int z=0;z<nz;z++)
        {
            int start=z*plane;
            for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
            {
                int i=start+y*nx+x;if(solid[i])continue;parent[i]=i;
                if(x==0||y==0||x==nx-1||y==ny-1||z==0)Join(i,outside);
                if(x>0&&!solid[i-1])Join(i,i-1);if(y>0&&!solid[i-nx])Join(i,i-nx);if(z>0&&!solid[i-plane])Join(i,i-plane);
            }
            int cups=0,material=0;for(int i=start;i<start+plane;i++)if(solid[i])material++;else if(Root(i)!=Root(outside))cups++;
            if(cups>0)cupLayers++;maxCup=Math.Max(maxCup,cups*cell*cell);peak=Math.Max(peak,material*cell*cell);
            Array.Clear(visited);
            for(int i=0;i<plane;i++)if(solid[start+i]&&!visited[i])
            {
                bool supported=false;visited[i]=true;queue.Enqueue(i);int first=i;
                while(queue.TryDequeue(out int j))
                {
                    if(z>0&&solid[start+j-plane])supported=true;
                    int x=j%nx,y=j/nx;
                    void Visit(int n){if(!visited[n]&&solid[start+n]){visited[n]=true;queue.Enqueue(n);}}
                    if(x>0)Visit(j-1);if(x+1<nx)Visit(j+1);if(y>0)Visit(j-nx);if(y+1<ny)Visit(j+nx);
                }
                // The first material layer is seated on the build platform; later disconnected starts need supports.
                bool firstLayer = z<=1;
                if(!supported&&!firstLayer){islands++;if(islandPoints.Count<500)islandPoints.Add(Point(first%nx,first/nx,z));}
            }
        }
        return new(cell,nz,voids,voidCells*cell*cell*cell,cupLayers,maxCup,islands,peak,voidPoints.Take(500).ToArray(),islandPoints.ToArray());
    }
}
