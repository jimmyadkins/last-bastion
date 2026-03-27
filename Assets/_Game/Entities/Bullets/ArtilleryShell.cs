using UnityEngine;

public class ArtilleryShell : Bullet
{
    protected override void SetInitialVelocity()
    {
        float verticalSpeed = MathFunctions.Sqrt2 / 2 * speed;
        Vector3 v = transform.forward * speed;
        v.y = verticalSpeed;
        rb.linearVelocity = v;
    }
}
