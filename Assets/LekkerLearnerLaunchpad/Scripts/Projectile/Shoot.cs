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

        [SerializeField]
        private float projectileLifeInSeconds;

        void Update()
        {
            if (Input.GetButtonDown("Fire1")){
                GameObject projectile = Instantiate(projectilePrefab);

                projectile.tag = Tags.BULLET_TAG;
                projectile.transform.position = transform.position;
                Rigidbody rigidbody = projectile.GetComponent<Rigidbody>();
                rigidbody.AddForce(transform.forward * shootingSpeed.GetSpeedValue(), ForceMode.Impulse);

                StartCoroutine(killProjectile(projectile));
            }            
        }

        private IEnumerator killProjectile(GameObject projectile)
        {
            yield return new WaitForSeconds(projectileLifeInSeconds);
            Destroy(projectile);
        }
    }
}
