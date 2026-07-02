using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// 通用血量组件：敌人和玩家都能用。管理血量、受伤、死亡 + 受击反馈(缩一下)
public class Health : MonoBehaviour
{
    public float maxHealth = 100f;
    public bool destroyOnDeath = true;  // 敌人=true(死了销毁)；玩家=false(死了重载场景重来)
    public bool invulnerable = false;   // 无敌(闪避无敌帧期间为true → 免疫伤害)
    public float squashScale = 0.8f;   // 受击瞬间缩到的比例(0.8=缩到80%)
    public float squashTime = 0.12f;   // 回弹到原大小的时间(秒)

    private float currentHealth;
    private Vector3 originalScale;
    private Coroutine squashRoutine;

    void Start()
    {
        currentHealth = maxHealth;
        originalScale = transform.localScale;
    }

    // 被攻击时调用：扣血 + 缩一下 + 判定死亡
    public void TakeDamage(float amount)
    {
        if (invulnerable) return;   // 无敌帧期间免疫伤害(闪避)
        currentHealth -= amount;
        Debug.Log(gameObject.name + " 受到 " + amount + " 伤害，剩余 " + currentHealth);

        if (currentHealth <= 0f)
        {
            Die();
            return;
        }

        // 受击反馈：缩一下再弹回(没死才做)
        if (squashRoutine != null) StopCoroutine(squashRoutine);
        squashRoutine = StartCoroutine(Squash());
    }

    // 协程：瞬间缩小 → 平滑弹回原大小
    IEnumerator Squash()
    {
        Vector3 small = originalScale * squashScale;
        transform.localScale = small;
        float t = 0f;
        while (t < squashTime)
        {
            t += Time.unscaledDeltaTime;   // 用不受顿帧影响的真实时间
            transform.localScale = Vector3.Lerp(small, originalScale, t / squashTime);
            yield return null;
        }
        transform.localScale = originalScale;
    }

    void Die()
    {
        Debug.Log(gameObject.name + " 死亡");
        if (destroyOnDeath)
            Destroy(gameObject);                                              // 敌人：直接销毁
        else
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);  // 玩家：重载当前场景=重来
    }
}
