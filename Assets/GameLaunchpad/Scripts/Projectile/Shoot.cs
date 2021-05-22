using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace com.coldcloudmedia
{
    public class Shoot : MonoBehaviour
    {
        [SerializeField]
        private GameObject projectilePrefab;

        [SerializeField]
        private ShootingSpeed shootingSpeed;

        void Update()
        {
            if (Input.GetButtonDown("Fire1")){
                GameObject projectile = Instantiate(projectilePrefab);

                projectile.tag = Tags.BULLET_TAG;
                projectile.transform.position = transform.position;
                Rigidbody rigidbody = projectile.GetComponent<Rigidbody>();
                rigidbody.AddForce(transform.forward * shootingSpeed.GetSpeedValue() * Time.deltaTime, ForceMode.Impulse);
            }            
        }
    }
}
