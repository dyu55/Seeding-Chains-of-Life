#!/bin/bash

# SCoL 项目自动监工模式
SESSION_ID="scol_dev_main"
SLEEP_TIME=10

echo "🚀 SCoL 自动化开发引擎已启动..."
echo "👁️  正在监视 scol_checklist.json 进度..."

while true; do
    # 捕获 Python 生成的纯净指令
    PROMPT=$(python ClawCommander.py)
    
    # 检查终止条件
    if [[ "$PROMPT" == "DONE" ]]; then
        echo "🎉 所有清单已清空，自动化下线！"
        break
    fi

    if [[ "$PROMPT" == ERROR* ]]; then
        echo "❌ $PROMPT"
        break
    fi

    # 提取当前任务的 ID 用于终端显示
    CURRENT_ID=$(echo "$PROMPT" | grep "ID:" | awk '{print $3}')
    
    echo "--------------------------------------------------"
    echo "⏳ [$(date +'%H:%M:%S')] 正在派发任务 [$CURRENT_ID] 给 Clawdbot..."
    
    # 核心修复：明确加上 --session-id 参数，让它拥有连续记忆
    clawdbot agent --session-id "$SESSION_ID" --message "$PROMPT"
    
    echo "💤 任务已发送。冷却 $SLEEP_TIME 秒后重新扫描..."
    sleep $SLEEP_TIME
done