---
name: flow
description: flow를 사용해서 상태 기반으로 동작한다.
argument-hint: 설계, 계획, 구현, 실행, 테스트, 기록
handoffs: 
  - label: 실행
    agent: flow.design
    prompt: "사용자에게 다음 요구사항을 전달 받자"
---
# Instructions

## 전체 철학

* **Flow의 목적**: 중단 없는 작업 연속 실행.
* 상태에서 명확하게 사용자의 개입이 필요하다고 지시 하지 않는 한 계속 작업 한다.

## 준비
* **초기화**: `.\flow.ps1 state IDLE --force`

## 실행

* `./flow state`로 현재 상태를 확인 후 agent_instruction을 참고 하여 다음 행동을 한다.