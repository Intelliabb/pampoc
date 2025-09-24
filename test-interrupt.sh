#!/bin/bash

API_BASE="http://localhost:5269/api"

echo "🚀 Testing Conversation Interrupt Flow"
echo "======================================"

# 1. Start conversation
echo "1. Starting conversation..."
CONV_RESPONSE=$(curl -s -X POST "$API_BASE/conversation/start" \
  -H "Content-Type: application/json" \
  -d '{"systemPrompt": "You are a helpful assistant."}')

CONV_ID=$(echo $CONV_RESPONSE | grep -o '"conversationId":"[^"]*"' | cut -d'"' -f4)
echo "   Conversation ID: $CONV_ID"

# 2. Start a long processing task in background
echo "2. Starting long conversation (in background)..."
curl -X POST "$API_BASE/conversation/$CONV_ID/continue" \
  -H "Content-Type: application/json" \
  -d '{"text": "Tell me a very detailed 500-word story about the history of computing, include lots of technical details and make it very comprehensive"}' &

CONTINUE_PID=$!
echo "   Started process PID: $CONTINUE_PID"

# 3. Wait a moment then interrupt
echo "3. Waiting 3 seconds..."
sleep 3

echo "4. Interrupting conversation..."
INTERRUPT_RESPONSE=$(curl -s -X POST "$API_BASE/conversation/$CONV_ID/interrupt" \
  -H "Content-Type: application/json" \
  -d '{"additionalPrompt": "Actually, make it about space exploration instead and keep it to 2 sentences"}')

echo "   Interrupt response: $INTERRUPT_RESPONSE"

# 4. Check final state
echo "5. Checking final state..."
sleep 1
STATE_RESPONSE=$(curl -s "$API_BASE/conversation/$CONV_ID/state")
echo "   Final state: $STATE_RESPONSE"

# 5. Clean up
echo "6. Cleaning up..."
kill $CONTINUE_PID 2>/dev/null || true
curl -s -X DELETE "$API_BASE/conversation/$CONV_ID" > /dev/null

echo "✅ Test completed!"