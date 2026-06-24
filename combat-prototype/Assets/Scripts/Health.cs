using System.Collections;
using UnityEngine;

// 挂在敌人身上：管理血量、受伤、死亡 + 受击反馈(闪红)
public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    public Color flashColor = Color.red;   // 受击闪烁的颜色
    public float flashTime = 0.1f;         // 闪烁持续时间(秒)

    private float currentHealth;
    private Renderer rend;
    private Color originalColor;

    void Start()
    {
        currentHealth = maxHealth;
        rend = GetComponentInChildren<Renderer>();        // 拿到模型的渲染器(胶囊自带)
        if (rend != null) originalColor = rend.material.color;
    }

    // 被攻击时调用：扣血 + 闪红 + 判定死亡
    public void TakeDamage(float amount)
    {
        currentHealth -= amount;
        Debug.Log(gameObject.name + " 受到 " + amount + " 伤害，剩余 " + currentHealth);

        if (rend != null) StartCoroutine(Flash());        // 受击反馈：闪一下

        if (currentHealth <= 0f) Die();
    }

    // 协程：把颜色变成flashColor，过flashTime秒再变回来
    IEnumerator Flash()
    {
        rend.material.color = flashColor;
        yield return new WaitForSeconds(flashTime);
        rend.material.color = originalColor;
    }

    void Die()
    {
        Debug.Log(gameObject.name + " 死亡");
        Destroy(gameObject);
    }
}
