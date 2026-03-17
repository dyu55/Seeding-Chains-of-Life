import json
import sys
import os

def get_next_prompt():
    file_path = 'scol_checklist.json'
    try:
        with open(file_path, 'r', encoding='utf-8') as f:
            data = json.load(f)
    except FileNotFoundError:
        return "ERROR: 找不到 scol_checklist.json"

    # 筛选出所有未完成的任务
    todo_tasks = [t for t in data['tasks'] if t['status'] == 'todo']

    if not todo_tasks:
        return "DONE"

    # 取最高优先级的任务
    current = todo_tasks[0]
    remaining = len(todo_tasks) - 1

    # 加入了强制修改 JSON 的指令
    prompt = f"""[系统自动督导指令] 当前项目: {data['project']} | 阶段: {data['phase']}
【当前唯一目标】 ID: {current['id']} - {current['task']}
【执行细节要求】 {current['detail']}

【执行规则】：
1. 请立即在 Unity 项目中完成上述任务，编写并保存代码。
2. 严格遵循 FPS 键鼠架构，遇到编译错误自行修复。
3. ⚠️【极其重要】：当你确信任务执行完毕且代码无报错时，你必须主动打开并修改项目根目录下的 `scol_checklist.json` 文件！
4. 你需要将 ID 为 "{current['id']}" 的任务节点中的 `"status": "todo"` 修改为 `"status": "done"`，并保存文件。

如果不修改 JSON 文件，系统会认为你没做完，并在 10 秒后重新派发该任务惩罚你。
(当前队列中还有 {remaining} 项任务待处理，快去干活。)"""
    
    return prompt

if __name__ == "__main__":
    print(get_next_prompt())