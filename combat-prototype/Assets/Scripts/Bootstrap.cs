using UnityEngine;
using UnityEngine.SceneManagement;

// 运行时自动创建三个辅助物体：CombatHUD（血条）、BossSpawner、PickupSpawner。
//
// 为什么不各自用 [RuntimeInitializeOnLoadMethod]：
//   那个特性一次 Play 会话只触发一次，**不会**因 SceneManager.LoadScene 再触发。
//   而玩家死亡时 Health.Die() 正是走 LoadScene 重载场景 —— 三个运行时创建的物体
//   会随旧场景一起销毁且永不重建，结果是「有任务栏、没血条、Boss 不刷、物品不撒」，
//   任务再也无法完成，只能退回编辑器重进 Play。录演示时死一次后面就全废了。
//
// 所以这里额外订阅 sceneLoaded（静态事件，跨场景存活）来补建。
static class Bootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Init()
    {
        SpawnAll();
        // 先退订再订阅：关闭 Reload Domain 后静态订阅会跨 Play 会话残留，
        // 用具名方法（而非匿名 lambda）才退得掉。
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => SpawnAll();

    static void SpawnAll()
    {
        // 没有玩家 = 不是战斗场景，不建（批量评估场景也不受干扰）
        if (GameObject.FindGameObjectWithTag("Player") == null) return;

        if (Object.FindAnyObjectByType<CombatHUD>()     == null) new GameObject("CombatHUD",     typeof(CombatHUD));
        if (Object.FindAnyObjectByType<BossSpawner>()   == null) new GameObject("BossSpawner",   typeof(BossSpawner));
        if (Object.FindAnyObjectByType<PickupSpawner>() == null) new GameObject("PickupSpawner", typeof(PickupSpawner));
    }
}
