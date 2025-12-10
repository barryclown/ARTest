using UnityEngine;

public class EnemyController : MonoBehaviour
{
    public Transform target;
    public float speed = 0.01f;

    [Header("點選框 (子物件 Plane)")]
    public GameObject selectPlane;   
    private bool isSelected = false; 

    private void Awake()
    {
        selectPlane = transform.Find("Plane").gameObject;
      
        if (selectPlane != null)
            selectPlane.SetActive(false);
    }

    public void Init(Transform target, float speed)
    {
        this.target = target;
        this.speed = speed;
    }

    private void Update()
    {
        if (target == null)
            return;

        float step = speed * Time.deltaTime;

        
        Vector3 targetPos = new Vector3(
            target.position.x,
            transform.position.y,
            target.position.z
        );

        transform.position = Vector3.MoveTowards(transform.position, targetPos, step);
    }

    // 手機單指點一下 / 滑鼠左鍵點一下
    private void OnMouseDown()
    {
        if (!isSelected)
        {
            // 第一次點：只顯示點選框
            isSelected = true;

            if (selectPlane != null)
                selectPlane.SetActive(true);
        }
        else
        {
            // 第二次點：刪掉整隻烏龜
            Destroy(gameObject);
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other.transform != target)
            return;

        SinglePlacementManager.instance.OnBaseHit();
        Destroy(gameObject);
    }
}
