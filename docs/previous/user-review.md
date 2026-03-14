# 스펙상세
* code references, conditions는 접을수 있으면 좋겠다.
* 실제 json에는 검토 내용이 표시 되는데 스펙 상세에선 표시 되지 않음
* 테스트 결과, 증거등이 시각화 되면 좋을 것 같다.
* 스펙의 컨디션을 삭제 할 수 있으면 좋겠다.

# agent 호출
worktree에서 모두 호출하면 워크트리의 정보를 넘길 필요가 없을지도?

# overwrap
Developer 호출이 끝났을 때,
Developer는 다음 스펙을 구현 하고
이전 Developer가 구현 한 내용은 Test Validator, Testor 순으로 호출 된다.
즉 Developer -> Test Validator -> Testor 순으로 동작 후 다시 Developer로 가던 흐름을
Developer가 끝나면 바로 다음 Developer 호출로 이어지고, 기존 스펙은 Test Validator -> Testor가 호출 된다.

