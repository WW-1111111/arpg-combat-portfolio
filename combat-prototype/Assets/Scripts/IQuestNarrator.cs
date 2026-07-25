using System;

// 叙事生成器的统一接口。B2(LLM) 和 B3(模板) 都实现它，所以可以互换、便于对比评估。
// 约定：输入“机制已固定的 Quest” + 世界背景，生成叙事后通过 onDone 回调返回填好的 Quest。
//       实现方只允许填叙事字段(title/description/objectiveText/completionText)，机制字段不动。
public interface IQuestNarrator
{
    void GenerateNarrative(Quest mechanicQuest, string worldContext, Action<Quest> onDone);
}
