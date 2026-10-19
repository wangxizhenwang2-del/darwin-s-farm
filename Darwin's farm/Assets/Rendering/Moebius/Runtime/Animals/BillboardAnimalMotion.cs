using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace Darwin.Rendering
{
    /// <summary>Presentation-only motion. Uses existing species frame sequences; never changes ecological state.</summary>
    [ExecuteAlways, DisallowMultipleComponent]
    public sealed class BillboardAnimalMotion : MonoBehaviour
    {
        public enum Medium { Swimming, Flying }
        public Medium medium;
        public PopulationAnimationSet animationSet;
        public SpriteRenderer image;
        public BillboardAnimalMotionVFX effects;
        public Vector3 routeCenter;
        public Vector2 routeRadius = new Vector2(3, 2);
        [Min(.01f)] public float speed = 1.6f;
        [Range(0,1)] public float routePhase;
        [Min(.1f)] public float spriteHeight = 3.5f;
        public bool artworkFacesLeft = true;
        [Range(0,.6f)] public float immersion = .25f;
        [Min(0)] public float bobAmplitude = .12f;
        [Min(0)] public float bankDegrees = 6;
        [Min(.1f)] public float frameRateMultiplier = 1;
        public bool showEffects = true;
        public Vector3 Velocity { get; private set; }
        public int CurrentFrame { get; private set; }
        public float CurrentTime { get; private set; }
        public float RouteLength => totalLength;
        const int Samples = 128;
        readonly float[] distances = new float[Samples + 1];
        float totalLength;
        Vector2 measuredRadius;
        MaterialPropertyBlock properties;

        void OnEnable() { BuildDistanceTable(); }
        void OnValidate() { routeRadius = Vector2.Max(routeRadius, Vector2.one * .1f); BuildDistanceTable(); }
        void BuildDistanceTable()
        {
            measuredRadius = routeRadius; distances[0] = 0;
            Vector3 previous = Ellipse(0);
            for (int i=1;i<=Samples;i++)
            {
                Vector3 point=Ellipse(i*Mathf.PI*2/Samples);
                distances[i]=distances[i-1]+Vector3.Distance(previous,point); previous=point;
            }
            totalLength=Mathf.Max(distances[Samples],.01f);
        }
        Vector3 Ellipse(float angle) => new Vector3(Mathf.Sin(angle)*routeRadius.x,0,Mathf.Cos(angle)*routeRadius.y);
        public void Pose(float time,out Vector3 position,out Vector3 forward)
        {
            if (totalLength<=0 || measuredRadius!=routeRadius) BuildDistanceTable();
            float distance=Mathf.Repeat(time*Mathf.Max(.01f,speed)+routePhase*totalLength,totalLength);
            int low=0,high=Samples;
            while(high-low>1) { int mid=(low+high)/2; if(distances[mid]<distance)low=mid;else high=mid; }
            float angle=(low+Mathf.InverseLerp(distances[low],distances[high],distance))*Mathf.PI*2/Samples;
            var parent=transform.parent;
            Vector3 local=routeCenter+Ellipse(angle);
            if(medium==Medium.Flying) local.y+=Mathf.Sin(time*1.8f+routePhase*6.28f)*bobAmplitude;
            position=parent?parent.TransformPoint(local):local;
            forward=new Vector3(Mathf.Cos(angle)*routeRadius.x,0,-Mathf.Sin(angle)*routeRadius.y).normalized;
            if(parent)forward=parent.TransformDirection(forward);
        }
        public void Evaluate(float time,Camera camera)
        {
            if(!image || !animationSet || !camera) return;
            CurrentTime=time;
            Pose(time,out Vector3 position,out Vector3 forward);
            Velocity=forward*speed;
            transform.position=position;
            bool common=animationSet.Mode==PopulationAnimationMode.CommonLoop;
            Sprite[] frames=common?animationSet.CommonFrames:animationSet.WalkFrames;
            if(frames==null||frames.Length==0)return;
            int start=common?animationSet.CommonStart:animationSet.WalkStart;
            int end=common?animationSet.CommonEnd:animationSet.WalkEnd;
            int fps=common?animationSet.CommonFps:animationSet.WalkFps;
            CurrentFrame=start+Mathf.FloorToInt((time+routePhase*3)*fps*frameRateMultiplier)%(end-start+1);
            if(CurrentFrame<start) CurrentFrame=start;
            image.sprite=frames[CurrentFrame];
            var reference=animationSet.IdleSprite?animationSet.IdleSprite:frames[start];
            float scale=spriteHeight/Mathf.Max(reference.bounds.size.y,.01f);
            image.transform.localScale=Vector3.one*scale;
            float facing=Vector3.Dot(forward,camera.transform.right);
            if(Mathf.Abs(facing)>.15f)image.flipX=artworkFacesLeft?facing>0:facing<0;
            float roll=medium==Medium.Flying?Mathf.Sin(time*speed/Mathf.Max(routeRadius.x,1)+routePhase*6.28f)*bankDegrees:0;
            image.transform.rotation=camera.transform.rotation*Quaternion.AngleAxis(roll,Vector3.forward);
            image.transform.position=position;
            if(medium==Medium.Swimming)
            {
                // Keep the waterline stable despite transparent canvas margins and changing fin silhouettes.
                float line=reference.bounds.min.y+reference.bounds.size.y*immersion;
                image.transform.position=position-camera.transform.up*(line*scale)+Vector3.up*(.018f+Mathf.Sin(time*2.1f+routePhase*6.28f)*bobAmplitude);
                if(properties==null)properties=new MaterialPropertyBlock();
                image.GetPropertyBlock(properties);properties.SetFloat("_WaterHeight",position.y);
                properties.SetFloat("_WaterSpriteLine",-10000);image.SetPropertyBlock(properties);
            }
            image.shadowCastingMode=ShadowCastingMode.Off;
            if(effects) { effects.gameObject.SetActive(showEffects); if(showEffects)effects.Evaluate(this,time,camera); }
        }
    }
}
