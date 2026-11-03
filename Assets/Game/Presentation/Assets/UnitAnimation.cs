using System.Collections.Generic;
using UnityEngine;

namespace Immortal.Presentation
{
    /// <summary>Мгновенные анимационные события из симуляции.</summary>
    public enum AnimEvent { Attack, Cast, Hit, Death, Revive }

    /// <summary>Непрерывное состояние юнита, которое вид передаёт аниматору каждый кадр.</summary>
    public struct UnitAnimState
    {
        public float Speed;          // м/с
        public bool Windup;          // замах (показанный удар)
        public float WindupProgress; // 0..1
        public bool Dead;
        public bool Gather;          // герой собирает ресурс
        public bool Mounted;
        public bool InCombat;
    }

    /// <summary>
    /// Аниматор вида. Реализации: ProceduralAnimator для примитивов и AnimatorBridge для моделей с Animator.
    /// Свой аниматор: реализуйте интерфейс в MonoBehaviour на префабе — ViewFactory найдёт его сам.
    /// </summary>
    public interface IUnitAnimator
    {
        void Tick(in UnitAnimState state, float dt);
        void Fire(AnimEvent e);
    }

    /// <summary>
    /// Мост к Animator модели. Стандартные параметры (все необязательны, отсутствующие пропускаются):
    /// float Speed; bool Windup, Dead, Gather, Mounted, InCombat; trigger Attack, Cast, Hit, Death, Revive.
    /// </summary>
    public sealed class AnimatorBridge : MonoBehaviour, IUnitAnimator
    {
        Animator _a;
        readonly HashSet<int> _params = new HashSet<int>();
        static readonly int SpeedH = Animator.StringToHash("Speed"), WindupH = Animator.StringToHash("Windup"), DeadH = Animator.StringToHash("Dead"),
            GatherH = Animator.StringToHash("Gather"), MountedH = Animator.StringToHash("Mounted"), CombatH = Animator.StringToHash("InCombat");

        public void Init(Animator a)
        {
            _a = a;
            foreach (var p in a.parameters) _params.Add(p.nameHash);
        }

        public void Tick(in UnitAnimState st, float dt)
        {
            if (_a == null) return;
            if (_params.Contains(SpeedH)) _a.SetFloat(SpeedH, st.Speed, 0.08f, dt);
            if (_params.Contains(WindupH)) _a.SetBool(WindupH, st.Windup);
            if (_params.Contains(DeadH)) _a.SetBool(DeadH, st.Dead);
            if (_params.Contains(GatherH)) _a.SetBool(GatherH, st.Gather);
            if (_params.Contains(MountedH)) _a.SetBool(MountedH, st.Mounted);
            if (_params.Contains(CombatH)) _a.SetBool(CombatH, st.InCombat);
        }

        public void Fire(AnimEvent e)
        {
            if (_a == null) return;
            int h = Animator.StringToHash(e.ToString());
            if (_params.Contains(h)) _a.SetTrigger(h);
        }
    }

    /// <summary>
    /// Процедурная анимация примитивов: шаг с покачиванием, дыхание, наклон на замахе, выпад при ударе,
    /// вздрагивание при попадании, падение при смерти, наклоны при сборе.
    /// </summary>
    public sealed class ProceduralAnimator : MonoBehaviour, IUnitAnimator
    {
        public Transform Pivot;      // что качаем (тело)
        public float BaseY;
        public bool Quadruped;
        float _phase, _attack, _hit, _cast, _death;
        Vector3 _baseScale = Vector3.one;

        public void Init(Transform pivot, bool quadruped)
        {
            Pivot = pivot; Quadruped = quadruped;
            BaseY = pivot.localPosition.y;
            _baseScale = pivot.localScale;
        }

        public void Tick(in UnitAnimState st, float dt)
        {
            if (Pivot == null) return;
            float speed = Mathf.Min(st.Speed, 10f);
            _phase += dt * (3f + speed * 1.6f);
            _attack = Mathf.Max(0, _attack - dt * 4f);
            _hit = Mathf.Max(0, _hit - dt * 6f);
            _cast = Mathf.Max(0, _cast - dt * 3f);
            _death = st.Dead ? Mathf.Min(1f, _death + dt * 2.5f) : 0f;

            float bob = speed > 0.3f ? Mathf.Abs(Mathf.Sin(_phase)) * 0.12f : Mathf.Sin(_phase * 0.5f) * 0.02f;
            float lean = 0f;            // градусы вперёд
            float side = speed > 0.3f ? Mathf.Sin(_phase) * 4f : 0f;
            if (st.Windup) lean -= 8f + st.WindupProgress * 12f; // отклон назад перед ударом
            lean += _attack * 28f;
            if (st.Gather) lean += 25f + Mathf.Sin(_phase * 2f) * 10f;
            lean += _cast * 10f;
            if (Quadruped) lean *= 0.4f;

            var squash = 1f - _hit * 0.18f;
            Pivot.localScale = new Vector3(_baseScale.x * (2f - squash), _baseScale.y * squash, _baseScale.z * (2f - squash));
            float baseY = BaseY + (st.Mounted ? 0.75f : 0f);
            if (_death > 0)
            {
                Pivot.localRotation = Quaternion.Euler((Quadruped ? 90f : 0f) + 0f, 0, 85f * _death);
                Pivot.localPosition = new Vector3(0, Mathf.Lerp(baseY, baseY * 0.35f, _death), 0);
                return;
            }
            Pivot.localPosition = new Vector3(0, baseY + bob + _cast * 0.25f, _attack * 0.35f);
            Pivot.localRotation = Quaternion.Euler((Quadruped ? 90f : 0f) + lean, 0, side);
        }

        public void Fire(AnimEvent e)
        {
            switch (e)
            {
                case AnimEvent.Attack: _attack = 1f; break;
                case AnimEvent.Cast: _cast = 1f; break;
                case AnimEvent.Hit: _hit = 1f; break;
                case AnimEvent.Revive: _death = 0; break;
            }
        }
    }
}
