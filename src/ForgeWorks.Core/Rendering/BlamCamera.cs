using System;
using System.Collections.Generic;
using System.Numerics;

namespace ForgeWorks.Core.Rendering;

/// <summary>
/// Orbit camera operating in Halo Blam! Right-Handed Z-Up space (X=Forward, Y=Left, Z=Up).
/// Produces OpenGL-convention matrices (clip-space depth -1..1) for use with
/// System.Numerics row-vector math uploaded untransposed to GLSL (uMVP * v).
/// </summary>
public sealed class BlamCamera
{
    public Vector3 Target { get; set; } = Vector3.Zero;
    public float Distance { get; set; } = 2500.0f;
    public float Yaw { get; set; } = 45.0f;   // Horizontal angle (degrees)
    public float Pitch { get; set; } = 30.0f; // Elevation angle (degrees)
    public float Fov { get; set; } = 70.0f;   // Vertical field of view (degrees)

    public Vector3 Position { get; private set; } = new(0, -2500, 1000);
    public Vector3 Forward { get; private set; } = Vector3.UnitX;
    public Vector3 Right { get; private set; } = -Vector3.UnitY;
    public Vector3 Up { get; private set; } = Vector3.UnitZ;

    public float NearPlane { get; private set; } = 1.0f;
    public float FarPlane { get; private set; } = 500000.0f;

    public Matrix4x4 ViewMatrix { get; private set; } = Matrix4x4.Identity;
    public Matrix4x4 ProjectionMatrix { get; private set; } = Matrix4x4.Identity;

    public bool IsFirstPerson { get; set; } = false;
    public Vector3 PlayerPosition { get; set; } = new(0, 0, 70.3f);

    public void UpdateMatrices(float viewportWidth, float viewportHeight)
    {
        Pitch = Math.Clamp(Pitch, -89.0f, 89.0f);

        float yawRad = Yaw * MathF.PI / 180.0f;
        float pitchRad = Pitch * MathF.PI / 180.0f;

        if (IsFirstPerson)
        {
            Position = PlayerPosition;
            Forward = new Vector3(
                MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                MathF.Sin(pitchRad));
            Target = Position + Forward;
            Right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));
            Up = Vector3.Normalize(Vector3.Cross(Right, Forward));

            NearPlane = 0.5f;
            FarPlane = 100000.0f;
        }
        else
        {
            Distance = Math.Clamp(Distance, 10.0f, 400000.0f);

            Vector3 offset = new(
                Distance * MathF.Cos(pitchRad) * MathF.Cos(yawRad),
                Distance * MathF.Cos(pitchRad) * MathF.Sin(yawRad),
                Distance * MathF.Sin(pitchRad));

            Position = Target + offset;
            Forward = Vector3.Normalize(Target - Position);
            Right = Vector3.Normalize(Vector3.Cross(Forward, Vector3.UnitZ));
            Up = Vector3.Normalize(Vector3.Cross(Right, Forward));

            NearPlane = MathF.Max(0.5f, Distance * 0.002f);
            FarPlane = MathF.Max(Distance * 400.0f, 50000.0f);
        }

        ViewMatrix = Matrix4x4.CreateLookAt(Position, Target, Vector3.UnitZ);

        float aspect = MathF.Max(viewportWidth / MathF.Max(viewportHeight, 1.0f), 0.1f);
        ProjectionMatrix = CreatePerspectiveGl(Fov * MathF.PI / 180.0f, aspect, NearPlane, FarPlane);
    }

    public void Walk(float forwardInput, float strafeInput, float speed = 15.0f)
    {
        float yawRad = Yaw * MathF.PI / 180.0f;
        var groundForward = new Vector3(MathF.Cos(yawRad), MathF.Sin(yawRad), 0);
        var groundRight = new Vector3(MathF.Sin(yawRad), -MathF.Cos(yawRad), 0);

        var move = (groundForward * forwardInput + groundRight * strafeInput) * speed;
        PlayerPosition += move;
    }

    /// <summary>
    /// Right-handed perspective projection mapping view-space depth to OpenGL NDC [-1, 1].
    /// (System.Numerics' CreatePerspectiveFieldOfView maps to D3D [0, 1], which clips the
    /// near half of the frustum under OpenGL.)
    /// </summary>
    public static Matrix4x4 CreatePerspectiveGl(float fovY, float aspect, float near, float far)
    {
        float f = 1.0f / MathF.Tan(fovY * 0.5f);
        return new Matrix4x4(
            f / aspect, 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, (2.0f * far * near) / (near - far), 0);
    }

    public void Orbit(float deltaYaw, float deltaPitch)
    {
        Yaw += deltaYaw;
        Pitch += deltaPitch;
    }

    public void Pan(float deltaX, float deltaY)
    {
        float speed = Distance * 0.0015f;
        Target += (-Right * deltaX * speed) + (Up * deltaY * speed);
    }

    public void Zoom(float delta)
    {
        Distance *= (1.0f - delta * 0.1f);
        Distance = Math.Clamp(Distance, 10.0f, 400000.0f);
    }

    /// <summary>Centers the orbit target on the given points and backs off so they fill the view.</summary>
    public void FrameBounds(IEnumerable<Vector3> points)
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        bool any = false;
        foreach (var p in points)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            any = true;
        }
        if (!any) return;

        Target = (min + max) * 0.5f;
        float radius = MathF.Max((max - min).Length() * 0.5f, 50.0f);
        float halfFov = Fov * MathF.PI / 360.0f;
        Distance = radius / MathF.Sin(halfFov) * 1.1f;
    }
}
