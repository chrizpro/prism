namespace Prism.Rendering;

static partial class Shaders
{
    const string SurfaceFunctions = @"
uniform int uLayers,uGradient,uGradientAxis,uWeather,uSeed;
uniform vec4 uGradientParams,uWeatherParams,uSurfaceParams;
uniform vec3 uColor2,uColor3,uColor4;
uniform float uTextureAngle,uWeatherHeight;
uniform vec4 uTexturePlacement;
uniform int uProjection,uTextureAxis,uTextureRepeat;
uniform vec2 uAnisotropy,uTransmission;
uniform vec3 uAbsorptionTint;
uniform int uHasColorMap,uHasRoughMap,uHasBumpMap,uHasDecalMap;
uniform sampler2D uColorMap,uRoughMap,uBumpMap,uDecalMap;
vec3 rotateSurface(vec3 p,float a){float c=cos(a),s=sin(a);return vec3(c*p.x+s*p.y,-s*p.x+c*p.y,p.z);}
float surfaceHash(vec3 p){
    p=mod(p,289.0);float n=mod((p.x*34.0+1.0)*p.x,289.0)+p.y;
    n=mod((n*34.0+1.0)*n,289.0)+p.z;
    return mod((n*34.0+1.0)*n,289.0)/289.0;
}
float surfaceNoise(vec3 p){
    vec3 i=floor(p),f=fract(p);f=f*f*(3.0-2.0*f);
    return mix(mix(mix(surfaceHash(i),surfaceHash(i+vec3(1,0,0)),f.x),mix(surfaceHash(i+vec3(0,1,0)),surfaceHash(i+vec3(1,1,0)),f.x),f.y),
               mix(mix(surfaceHash(i+vec3(0,0,1)),surfaceHash(i+vec3(1,0,1)),f.x),mix(surfaceHash(i+vec3(0,1,1)),surfaceHash(i+vec3(1,1,1)),f.x),f.y),f.z);
}
vec3 gradientColor(vec3 local,vec3 base){
    if(uGradient==0)return base;
    vec3 p=rotateSurface(local,uGradientParams.z);
    if(uGradientAxis==0)p=p.yzx;else if(uGradientAxis==1)p=p.xzy;
    float size=max(uGradientParams.x,0.1),off=uGradientParams.y;
    float t=uGradient==1?(p.z-off)/size:(uGradient==2?length(p.xy):length(p))/size-off/size;
    t=smoothstep(0.0,1.0,t);
    if(uGradient!=4)return mix(base,uColor2,t);
    vec2 uv=smoothstep(vec2(0),vec2(1),(p.xy-off)/size);
    return mix(mix(base,uColor2,uv.x),mix(uColor3,uColor4,uv.x),uv.y);
}
float weatherMask(vec3 local){
    if(uWeather==0||uWeatherParams.x<=0.0)return 0.0;
    vec3 p=local/max(uWeatherParams.y,0.1)+vec3(float(uSeed)*0.173,0.37,1.13);
    float n=0.65*surfaceNoise(p)+0.25*surfaceNoise(p*2.03)+0.1*surfaceNoise(p*5.17);
    float t=clamp((uWeatherParams.x*1.2-0.1-n)/0.12+0.5,0.0,1.0);t=t*t*(3.0-2.0*t);
    return t*(uWeatherParams.w>0.5?clamp(1.0-local.z/max(uWeatherHeight,0.1),0.0,1.0):1.0);
}
vec3 textureAxis(vec3 p){if(uTextureAxis==0)return p.yzx;if(uTextureAxis==1)return p.xzy;return p;}
vec3 mapSample(sampler2D img,vec2 uv){
    uv.y/=uTexturePlacement.w;
    vec2 dx=dFdx(uv),dy=dFdy(uv);
    if(uTextureRepeat==1)uv=fract(uv);
    else {vec2 halfPixel=0.5/vec2(textureSize(img,0));uv=clamp(uv,halfPixel,1.0-halfPixel);}
    return textureGrad(img,uv,dx,dy).rgb;
}
vec3 surfaceImage(sampler2D img,vec3 local,vec3 normal){
    vec3 p=textureAxis(rotateSurface((local-uTexturePlacement.xyz)/uSurfaceParams.w,uTextureAngle));
    vec3 n=textureAxis(rotateSurface(normal,uTextureAngle));
    if(uProjection==1)return mapSample(img,p.xy);
    if(uProjection==2)return mapSample(img,vec2(atan(p.y,p.x)/6.28318530718+0.5,p.z));
    vec3 w=abs(n);w*=w;w*=w;w/=max(w.x+w.y+w.z,0.000001);
    return mapSample(img,p.yz)*w.x+mapSample(img,p.xz)*w.y+mapSample(img,p.xy)*w.z;
}
float surfaceHeight(vec3 local,vec3 normal){
    if(uHasBumpMap==1)return dot(surfaceImage(uBumpMap,local,normal),vec3(0.2126,0.7152,0.0722));
    return surfaceNoise(local/uWeatherParams.y*8.0+vec3(float(uSeed)));
}
void surfaceLayers(inout vec3 base,inout vec3 N,inout vec2 mat){
    if(uLayers==0)return;
    vec3 local=vPos-uPatternOrigin;
    base=gradientColor(local,base);

    if(uHasColorMap==1)base*=surfaceImage(uColorMap,local,N);
    if(uHasDecalMap==1){
        vec3 dp=textureAxis(rotateSurface((local-uTexturePlacement.xyz)/uSurfaceParams.w,uTextureAngle));
        vec2 duv=vec2(dp.x,dp.y/uTexturePlacement.w);
        if(uTextureRepeat==1||!(duv.x<0.0||duv.x>1.0||duv.y<0.0||duv.y>1.0)){
            vec2 ddx=dFdx(duv),ddy=dFdy(duv);vec2 tuv=uTextureRepeat==1?fract(duv):duv;
            vec4 d=textureGrad(uDecalMap,tuv,ddx,ddy);base=mix(base,d.rgb,d.a);
        }
    }
    float rough=1.0-mat.y;
    if(uHasRoughMap==1)rough=dot(surfaceImage(uRoughMap,local,N),vec3(0.2126,0.7152,0.0722));
    float mask=weatherMask(local),detail=surfaceNoise(local/uWeatherParams.y*3.1+vec3(float(uSeed)));
    vec3 weather=uWeather==2?mix(vec3(0.18,0.055,0.018),vec3(0.65,0.28,0.075),detail):mix(vec3(0.12,0.095,0.065),vec3(0.37,0.30,0.20),detail);
    base=mix(base,weather,mask);mat.x*=1.0-mask;rough=mix(rough,uWeatherParams.z,mask);mat.y=1.0-clamp(rough,0.06,1.0);
    if(uSurfaceParams.x>0.0){
        float e=uHasBumpMap==1?uSurfaceParams.w/float(max(textureSize(uBumpMap,0).x,textureSize(uBumpMap,0).y)):uWeatherParams.y/8.0;
        e=max(e*0.25,0.001);
        vec3 g=vec3(surfaceHeight(local+vec3(e,0,0),N)-surfaceHeight(local-vec3(e,0,0),N),surfaceHeight(local+vec3(0,e,0),N)-surfaceHeight(local-vec3(0,e,0),N),surfaceHeight(local+vec3(0,0,e),N)-surfaceHeight(local-vec3(0,0,e),N))/(2.0*e);
        N=normalize(N-(g-N*dot(g,N))*uSurfaceParams.x*0.2);
    }
}
";
}
