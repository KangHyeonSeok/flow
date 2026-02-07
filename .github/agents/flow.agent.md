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
* 사용자 개입은 최소화한다.

## 실행

* `./flow state`로 현재 상태를 확인 후 agent_instruction을 참고 하여 다음 행동을 한다.