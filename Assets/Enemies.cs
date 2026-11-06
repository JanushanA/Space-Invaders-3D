using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Enemies : MonoBehaviour
{
    public float health = 50f;
    public void TakeDamage(float amount)
    {
        // Remove the damage of the weapon from health

        health -= amount;

        Debug.Log(health);
        if (health <= 0f)
        {

            Die();

        }
    }
    // Start is called before the first frame update
    void Start()
    {

    }

    // Update is called once per frame
    void Update()
    {

    }
    void Die()
    {

        Destroy(gameObject);

    }
}
