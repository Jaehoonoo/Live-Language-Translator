#pragma once

#include "../C/Baselib_HighCapacitySemaphore.h"
#include "Time.h"

namespace baselib
{
    BASELIB_CPP_INTERFACE
    {
        // baselib::HighCapacitySemaphore is similar to baselib::Semaphore but allows for far greater token count.
        // It is suitable to be used as resource counting semaphore.
        class HighCapacitySemaphore
        {
        public:
            // non-copyable
            HighCapacitySemaphore(const HighCapacitySemaphore& other) = delete;
            HighCapacitySemaphore& operator=(const HighCapacitySemaphore& other) = delete;

            // non-movable (strictly speaking not needed but listed to signal intent)
            HighCapacitySemaphore(HighCapacitySemaphore&& other) = delete;
            HighCapacitySemaphore& operator=(HighCapacitySemaphore&& other) = delete;

            // This is the max number of tokens guaranteed to be held by the semaphore at
            // any given point in time. Tokens submitted that exceed this value may silently
            // be discarded.
            enum : int64_t { MaxGuaranteedCount = Baselib_HighCapacitySemaphore_MaxGuaranteedCount };

            // Creates a counting semaphore synchronization primitive.
            // If there are not enough system resources to create a semaphore, process abort is triggered.
            HighCapacitySemaphore()
            {
                Baselib_HighCapacitySemaphore_CreateInplace(&m_SemaphoreData);
            }

            // Reclaim resources and memory held by the semaphore.
            //
            // If threads are waiting on the semaphore, destructor will trigger an assert and may cause process abort.
            ~HighCapacitySemaphore()
            {
                Baselib_HighCapacitySemaphore_FreeInplace(&m_SemaphoreData);
            }

            // Wait for semaphore token to become available
            //
            // This function is guaranteed to emit an acquire barrier.
            //
            // \param maxSpinCount  Max number of times to spin in user space before falling back to the kernel. The actual number
            //                      may differ depending on the underlying implementation but will never exceed the maxSpinCount
            //                      value.
            inline void Acquire(const uint32_t maxSpinCount = 0)
            {
                if (maxSpinCount && Baselib_HighCapacitySemaphore_TrySpinAcquire(&m_SemaphoreData, maxSpinCount))
                    return;

                return Baselib_HighCapacitySemaphore_Acquire(&m_SemaphoreData);
            }

            // Try to consume a token.
            //
            // When successful this function is guaranteed to emit an acquire barrier.
            //
            // \param maxSpinCount  Max number of times to spin in user space before falling back to the kernel. The actual number
            //                      may differ depending on the underlying implementation but will never exceed the maxSpinCount
            //                      value.
            // \returns             true if token was consumed. false if not.
            inline bool TryAcquire(const uint32_t maxSpinCount = 0)
            {
                return Baselib_HighCapacitySemaphore_TrySpinAcquire(&m_SemaphoreData, maxSpinCount);
            }

            // Wait for semaphore token to become available
            //
            // When successful this function is guaranteed to emit an acquire barrier.
            //
            // TryAcquire with a zero timeout differs from TryAcquire() in that TryAcquire() is guaranteed to be a user space operation
            // while Acquire with a zero timeout may enter the kernel and cause a context switch.
            //
            // Timeout passed to this function may be subject to system clock resolution.
            // If the system clock has a resolution of e.g. 16ms that means this function may exit with a timeout error 16ms earlier than originally scheduled.
            //
            // \param timeout       Time to wait for token to become available.
            // \param maxSpinCount  Max number of times to spin in user space before falling back to the kernel. The actual number
            //                      may differ depending on the underlying implementation but will never exceed the maxSpinCount
            //                      value.
            // \returns             true if token was consumed. false if timeout was reached.
            inline bool TryTimedAcquire(const timeout_ms timeoutInMilliseconds, const uint32_t maxSpinCount = 0)
            {
                if (maxSpinCount && Baselib_HighCapacitySemaphore_TrySpinAcquire(&m_SemaphoreData, maxSpinCount))
                    return true;

                return Baselib_HighCapacitySemaphore_TryTimedAcquire(&m_SemaphoreData, timeoutInMilliseconds.count());
            }

            // Submit tokens to the semaphore.
            //
            // When successful this function is guaranteed to emit a release barrier.
            //
            // Increase the number of available tokens on the semaphore by `count`. Any waiting threads will be notified there are new tokens available.
            // If count reach `Baselib_HighCapacitySemaphore_MaxGuaranteedCount` this function may silently discard any overflow.
            inline void Release(uint32_t count = 1)
            {
                return Baselib_HighCapacitySemaphore_Release(&m_SemaphoreData, count);
            }

            // Sets the semaphore token count to zero and release all waiting threads.
            //
            // When successful this function is guaranteed to emit a release barrier.
            //
            // Return:          number of released threads.
            inline uint64_t ResetAndReleaseWaitingThreads()
            {
                return Baselib_HighCapacitySemaphore_ResetAndReleaseWaitingThreads(&m_SemaphoreData);
            }

        private:
            Baselib_HighCapacitySemaphore m_SemaphoreData;
        };
    }
}
