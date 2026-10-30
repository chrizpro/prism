// UV layouts and tolerance-checked B-rep morphing. Included by kernel.cpp.
static TopoDS_Face mappingFace(const TopoDS_Shape& shape) {
    TopExp_Explorer ex(shape,TopAbs_FACE);
    if(!ex.More()) throw Standard_Failure("Select one reference face.");
    auto face=TopoDS::Face(ex.Current());ex.Next();
    if(ex.More()) throw Standard_Failure("Select one face rather than an entire multi-face body.");
    return face;
}

CK_API int ck_wire_sample(void* input,double tolerance,double* output,int capacity) {
    TRY
        auto wire=TopoDS::Wire(get(input));std::vector<gp_Pnt> points;
        for(BRepTools_WireExplorer ex(wire);ex.More();ex.Next()) {
            auto edge=ex.Current();BRepAdaptor_Curve curve(edge);GCPnts_QuasiUniformDeflection samples(curve,std::max(1e-6,tolerance));
            if(!samples.IsDone())throw Standard_Failure("Could not sample the flat outline.");
            for(int i=1;i<samples.NbPoints();i++)points.push_back(samples.Value(edge.Orientation()==TopAbs_REVERSED?samples.NbPoints()+1-i:i));
        }
        if(output && capacity>=int(points.size()))for(int i=0;i<int(points.size());i++){output[3*i]=points[i].X();output[3*i+1]=points[i].Y();output[3*i+2]=points[i].Z();}
        return int(points.size());
    CATCH(0)
}

CK_API void* ck_surface_unwrap(void* input,double x,double y,double* dimensions) {
    TRY
        auto face=mappingFace(get(input));
        BRepAdaptor_Surface surface(face);double u0,u1,v0,v1;
        BRepTools::UVBounds(face,u0,u1,v0,v1);
        double width=0,height=0;const int samples=256;
        for(int i=1;i<=samples;i++) {
            width+=surface.Value(u0+(u1-u0)*(i-1)/samples,(v0+v1)/2).Distance(surface.Value(u0+(u1-u0)*i/samples,(v0+v1)/2));
            height+=surface.Value((u0+u1)/2,v0+(v1-v0)*(i-1)/samples).Distance(surface.Value((u0+u1)/2,v0+(v1-v0)*i/samples));
        }
        if(surface.GetType()==GeomAbs_Cylinder) {width=surface.Cylinder().Radius()*(u1-u0);height=v1-v0;}
        if(width<1e-7||height<1e-7)throw Standard_Failure("The selected face has a collapsed UV layout; split it into regular patches first.");
        dimensions[0]=width;dimensions[1]=height;
        Handle(Geom_Plane) plane=new Geom_Plane(gp::XOY());
        auto mapWire=[&](const TopoDS_Wire& wire) {
            BRepBuilderAPI_MakeWire result;
            for(BRepTools_WireExplorer ex(wire,face);ex.More();ex.Next()) {
                auto edge=ex.Current();double a,b;auto curve=BRep_Tool::CurveOnSurface(edge,face,a,b);
                if(curve.IsNull())throw Standard_Failure("An edge has no surface coordinates.");
                auto spline=Geom2dConvert::CurveToBSplineCurve(new Geom2d_TrimmedCurve(curve,a,b));
                for(int i=1;i<=spline->NbPoles();i++) {auto p=spline->Pole(i);spline->SetPole(i,gp_Pnt2d(x+(p.X()-u0)*width/(u1-u0),y+(p.Y()-v0)*height/(v1-v0)));}
                BRepBuilderAPI_MakeEdge make(spline,plane);if(!make.IsDone())throw Standard_Failure("Cannot unwrap a boundary.");
                auto mapped=make.Edge();mapped.Orientation(edge.Orientation());result.Add(mapped);
            }
            if(!result.IsDone())throw Standard_Failure("The unwrapped boundary is disconnected.");
            return result.Wire();
        };
        auto outer=BRepTools::OuterWire(face);BRepBuilderAPI_MakeFace result(plane,mapWire(outer),true);
        for(TopExp_Explorer ex(face,TopAbs_WIRE);ex.More();ex.Next())if(!ex.Current().IsSame(outer))result.Add(mapWire(TopoDS::Wire(ex.Current())));
        auto flat=result.Face();BRepLib::BuildCurves3d(flat);
        ShapeFix_Face fix(flat);fix.Perform();flat=fix.Face();
        if(!BRepCheck_Analyzer(flat).IsValid())throw Standard_Failure("The UV layout overlaps or has invalid boundaries; split the face at a seam first.");
        return wrap(flat);
    CATCH(nullptr)
}

class SurfaceMorph : public BRepTools_Modification {
    std::function<gp_Pnt(const gp_Pnt&)> map;
    double tolerance;
public:
    SurfaceMorph(std::function<gp_Pnt(const gp_Pnt&)> m,double tol):map(m),tolerance(tol){}
    bool NewPoint(const TopoDS_Vertex& v,gp_Pnt& p,double& tol) override {p=map(BRep_Tool::Pnt(v));tol=tolerance;return true;}
    bool NewSurface(const TopoDS_Face& f,Handle(Geom_Surface)& s,TopLoc_Location& loc,double& tol,bool& reverseWires,bool& reverseFace) override {
        BRepAdaptor_Surface source(f);double a,b,c,d;BRepTools::UVBounds(f,a,b,c,d);
        for(int n=8;n<=128;n*=2) {
            TColgp_Array2OfPnt points(1,n+1,1,n+1);
            for(int i=0;i<=n;i++)for(int j=0;j<=n;j++)points.SetValue(i+1,j+1,map(source.Value(a+(b-a)*i/n,c+(d-c)*j/n)));
            GeomAPI_PointsToBSplineSurface fit;fit.Interpolate(points,Approx_IsoParametric);
            if(!fit.IsDone())throw Standard_Failure("Surface interpolation failed.");
            auto spline=fit.Surface();
            TColStd_Array1OfReal uk(1,spline->NbUKnots()),vk(1,spline->NbVKnots());spline->UKnots(uk);spline->VKnots(vk);
            double us=uk(1),ue=uk(uk.Upper()),vs=vk(1),ve=vk(vk.Upper());
            for(int i=1;i<=uk.Upper();i++)uk(i)=a+(b-a)*(uk(i)-us)/(ue-us);
            for(int i=1;i<=vk.Upper();i++)vk(i)=c+(d-c)*(vk(i)-vs)/(ve-vs);
            spline->SetUKnots(uk);spline->SetVKnots(vk);
            double error=0;
            for(int i=0;i<n;i++)for(int j=0;j<n;j++) {double u=a+(b-a)*(i+.5)/n,v=c+(d-c)*(j+.5)/n;error=std::max(error,spline->Value(u,v).Distance(map(source.Value(u,v))));}
            if(error<=tolerance*.25) {s=spline;loc=TopLoc_Location();tol=tolerance;reverseWires=false;reverseFace=false;return true;}
        }
        throw Standard_Failure("Surface mapping could not meet the requested tolerance. Split the object or use a larger tolerance.");
    }
    bool NewCurve(const TopoDS_Edge& e,Handle(Geom_Curve)& curve,TopLoc_Location& loc,double& tol) override {
        BRepAdaptor_Curve source(e);double a=source.FirstParameter(),b=source.LastParameter();
        if(BRep_Tool::Degenerated(e))return false;
        for(int n=8;n<=1024;n*=2) {
            Handle(TColgp_HArray1OfPnt) points=new TColgp_HArray1OfPnt(1,n+1);
            Handle(TColStd_HArray1OfReal) params=new TColStd_HArray1OfReal(1,n+1);
            for(int i=0;i<=n;i++) {double t=a+(b-a)*i/n;params->SetValue(i+1,t);points->SetValue(i+1,map(source.Value(t)));}
            GeomAPI_Interpolate fit(points,params,false,1e-9);fit.Perform();if(!fit.IsDone())throw Standard_Failure("Curve mapping failed.");
            auto spline=fit.Curve();double error=0;
            for(int i=0;i<n;i++){double t=a+(b-a)*(i+.5)/n;error=std::max(error,spline->Value(t).Distance(map(source.Value(t))));}
            if(error<=tolerance*.25){curve=spline;loc=TopLoc_Location();tol=tolerance;return true;}
        }
        throw Standard_Failure("Curve mapping could not meet the requested tolerance.");
    }
    bool NewCurve2d(const TopoDS_Edge& e,const TopoDS_Face& f,const TopoDS_Edge&,const TopoDS_Face&,Handle(Geom2d_Curve)& c,double& tol) override {
        double a,b;c=BRep_Tool::CurveOnSurface(e,f,a,b);tol=tolerance;return !c.IsNull();
    }
    bool NewParameter(const TopoDS_Vertex& v,const TopoDS_Edge& e,double& p,double& tol) override {p=BRep_Tool::Parameter(v,e);tol=tolerance;return true;}
    GeomAbs_Shape Continuity(const TopoDS_Edge&,const TopoDS_Face&,const TopoDS_Face&,const TopoDS_Edge&,const TopoDS_Face&,const TopoDS_Face&) override {return GeomAbs_C0;}
};

// settings: U/V/N scale, U/V/N offsets (mm), mirror U, swap UV, flip normal, tolerance.
CK_API void* ck_surface_wrap(void* input,void* reference,void* target,const double* settings) {
    TRY
        auto flat=mappingFace(get(reference)),face=mappingFace(get(target));BRepAdaptor_Surface source(flat),destination(face);
        if(source.GetType()!=GeomAbs_Plane)throw Standard_Failure("The reference face must be planar.");
        double a,b,c,d,u0,u1,v0,v1;BRepTools::UVBounds(flat,a,b,c,d);BRepTools::UVBounds(face,u0,u1,v0,v1);
        if(b-a<1e-9||d-c<1e-9)throw Standard_Failure("The reference layout has no area.");
        auto plane=source.Plane();auto axes=plane.Position();
        auto map=[&](const gp_Pnt& p) {
            gp_Vec delta(axes.Location(),p);double x=delta.Dot(gp_Vec(axes.XDirection())),y=delta.Dot(gp_Vec(axes.YDirection())),z=delta.Dot(gp_Vec(axes.Direction()));
            double u=((x-a)*settings[0]+settings[3])/(b-a),v=((y-c)*settings[1]+settings[4])/(d-c);
            if(settings[6]>.5)u=1-u;if(settings[7]>.5)std::swap(u,v);
            if(u< -1e-7||u>1+1e-7||v< -1e-7||v>1+1e-7)throw Standard_Failure("Mapped geometry extends outside the reference layout. Adjust scale or offsets, or split geometry at the seam.");
            gp_Pnt point;gp_Vec du,dv;destination.D1(u0+(u1-u0)*u,v0+(v1-v0)*v,point,du,dv);auto normal=du.Crossed(dv);
            if(normal.Magnitude()<1e-10)throw Standard_Failure("The target contains a singularity. Split it into regular patches.");
            normal.Normalize();if(face.Orientation()==TopAbs_REVERSED)normal.Reverse();if(settings[8]>.5)normal.Reverse();
            return point.Translated(normal*(z*settings[2]+settings[5]));
        };
        double tolerance=std::max(1e-6,settings[9]);
        Handle(SurfaceMorph) modification=new SurfaceMorph(map,tolerance);
        auto original=get(input);BRepTools_Modifier morph(original,modification);
        if(!morph.IsDone())throw Standard_Failure("Surface mapping failed.");
        auto result=morph.ModifiedShape(original);BRepLib::SameParameter(result,tolerance,true);
        if((face.Orientation()==TopAbs_REVERSED) ^ (settings[6]>.5) ^ (settings[7]>.5) ^ (settings[8]>.5))result.Reverse();
        if(!BRepCheck_Analyzer(result).IsValid())throw Standard_Failure("The mapped object is invalid. Reduce thickness, split at seams, or adjust the mapping.");
        return wrap(result);
    CATCH(nullptr)
}

// Rotation-minimizing frames avoid the flips of a Frenet frame at inflections.
struct MappingPath {
    Handle(Geom_Curve) curve;
    double first,last,length;
    std::vector<double> distances;
    std::vector<gp_Vec> normals;
    static constexpr int count=1024;
    MappingPath(const TopoDS_Shape& shape) {
        TopExp_Explorer ex(shape,TopAbs_EDGE);if(!ex.More())throw Standard_Failure("Select one reference curve.");
        auto edge=TopoDS::Edge(ex.Current());ex.Next();if(ex.More())throw Standard_Failure("Join the reference into one spline before curve wrapping.");
        curve=BRep_Tool::Curve(edge,first,last);if(curve.IsNull())throw Standard_Failure("The reference has no curve.");
        gp_Pnt point;gp_Vec previous;curve->D1(first,point,previous);previous.Normalize();
        gp_Vec up(0,0,1);if(std::abs(up.Dot(previous))>.95)up=gp_Vec(0,1,0);
        gp_Vec normal=up-previous*up.Dot(previous);normal.Normalize();
        distances.push_back(0);normals.push_back(normal);auto old=point;length=0;
        for(int i=1;i<=count;i++) {
            gp_Vec tangent;curve->D1(first+(last-first)*i/count,point,tangent);tangent.Normalize();
            gp_Quaternion rotation(previous,tangent);normal=rotation*normal;normal-=tangent*normal.Dot(tangent);normal.Normalize();
            length+=old.Distance(point);distances.push_back(length);normals.push_back(normal);old=point;previous=tangent;
        }
        if(length<1e-7)throw Standard_Failure("The reference curve is too short.");
    }
    double fraction(double parameter) const {
        double index=std::clamp((parameter-first)/(last-first)*count,0.,double(count));int i=std::min(count-1,int(index));
        return (distances[i]+(distances[i+1]-distances[i])*(index-i))/length;
    }
    double parameter(double fraction) const {
        double distance=std::clamp(fraction,0.,1.)*length;auto it=std::lower_bound(distances.begin(),distances.end(),distance);
        int i=std::clamp(int(it-distances.begin())-1,0,count-1);double blend=(distance-distances[i])/(distances[i+1]-distances[i]);
        return first+(last-first)*(i+blend)/count;
    }
    void frame(double t,gp_Pnt& p,gp_Vec& across,gp_Vec& normal) const {
        gp_Vec tangent;curve->D1(t,p,tangent);tangent.Normalize();
        double index=std::clamp((t-first)/(last-first)*count,0.,double(count));int i=std::min(count-1,int(index));
        normal=normals[i]*(1-(index-i))+normals[i+1]*(index-i);normal-=tangent*normal.Dot(tangent);normal.Normalize();across=normal.Crossed(tangent);across.Normalize();
    }
};

CK_API void* ck_curve_wrap(void* input,void* reference,void* target,const double* settings) {
    TRY
        MappingPath source(get(reference)),destination(get(target));
        auto map=[&](const gp_Pnt& p) {
            GeomAPI_ProjectPointOnCurve projection(p,source.curve,source.first,source.last);
            if(projection.NbPoints()==0)throw Standard_Failure("Cannot locate geometry on the reference curve.");
            double t=projection.LowerDistanceParameter();gp_Pnt base;gp_Vec across,normal;source.frame(t,base,across,normal);gp_Vec offset(base,p);
            double u=source.fraction(t)*settings[0]+settings[3]/source.length;
            double v=offset.Dot(across)*settings[1]+settings[4],n=offset.Dot(normal)*settings[2]+settings[5];
            if(settings[6]>.5)u=1-u;if(settings[7]>.5)std::swap(v,n);if(settings[8]>.5)n=-n;
            if(u< -1e-7||u>1+1e-7)throw Standard_Failure("Geometry extends beyond the mapped curve. Adjust the along-curve scale or offset.");
            gp_Pnt result;destination.frame(destination.parameter(u),result,across,normal);return result.Translated(across*v+normal*n);
        };
        auto original=get(input);Handle(SurfaceMorph) modification=new SurfaceMorph(map,std::max(1e-6,settings[9]));BRepTools_Modifier morph(original,modification);
        if(!morph.IsDone())throw Standard_Failure("Curve wrapping failed.");auto result=morph.ModifiedShape(original);BRepLib::SameParameter(result,settings[9],true);
        if((settings[6]>.5) ^ (settings[7]>.5) ^ (settings[8]>.5))result.Reverse();
        if(!BRepCheck_Analyzer(result).IsValid())throw Standard_Failure("The wrapped object is invalid. Reduce its cross section or split the path.");
        return wrap(result);
    CATCH(nullptr)
}
