using UnityEngine;
using System.Collections;

public class spawnWithoutTouchNewBehaviourScript : MonoBehaviour {


    public GameObject smStar;
    public GameObject midStar;
    // The medium star dust (Resources/Prefabs/CreditStarMd); not wired in the scene, loaded in Start when empty.
    public GameObject medStar;
    public GameObject SuperStar;
    public static float atomDelayTimer;
    public static float smStarTimer;
    public static float midStarTimer;
    public static float medStarTimer;

    public const string MediumPrefab = "prefabs/CreditStarMd";

    // used for random int for generating stars
    int max;

    // Use this for initialization
    void Start()
    {

        smStarTimer = 0;
        midStarTimer = 2f;
        medStarTimer = 3.5f;
        atomDelayTimer = 5f;
        if (medStar == null) medStar = Resources.Load<GameObject>(MediumPrefab);
    }

    // Update is called once per frame
    void Update()
    {
        
        spawn();


    }
    void spawn()
    {
        smStarTimer -= Time.deltaTime;
        midStarTimer -= Time.deltaTime;
        medStarTimer -= Time.deltaTime;
        atomDelayTimer -= Time.deltaTime;

        if (smStarTimer <= 0)
        {
            smStarTimer = Random.Range(5f, 7f);
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
            // spawn up tp 5 sm stars in a row for collecting
            max = Random.Range(1, 2);
            for (int i = 0; i < max; i++)
            {
                spawnSmStar(i, randomStarPos);
            }


        }
        if (midStarTimer <= 0)
        {

            midStarTimer = Random.Range(5f, 10f);
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
            // spawn up to 2-4 mid stars for collecting
            max = Random.Range(1, 2);
            for (int i = 0; i < max; i++)
            {
                spawnMidStar(i, randomStarPos);
            }
        }
        if (medStarTimer <= 0 && medStar != null)
        {
            // the medium dust, between the small and the large stars
            medStarTimer = Random.Range(4f, 8f);
            Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
            Instantiate(medStar, randomStarPos, transform.rotation);
        }
        if (atomDelayTimer <= 0)
        {
            atomDelayTimer = Random.Range(7f, 12f);
            spawnAtom();
        }

    }
    void spawnSmStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(vPos.x, vPos.y + pos, vPos.z);
        Instantiate(smStar, spawner, transform.rotation);

    }

    void spawnMidStar(int pos, Vector3 vPos)
    {
        Vector3 spawner = new Vector3(vPos.x, vPos.y + pos, vPos.z);
        Instantiate(midStar, spawner, transform.rotation);


    }
    // will make you invensiable for a few seconds.
    void spawnAtom()
    {
        Vector3 randomStarPos = new Vector3(Random.Range(-RailInset.PickupLaneHalf, RailInset.PickupLaneHalf), transform.position.y, transform.rotation.z);
        Instantiate(SuperStar, randomStarPos, transform.rotation);
    }
}


