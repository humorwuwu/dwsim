#!/usr/bin/env python3
"""Delay and drop sensor frames on the Modbus input-register path."""

import random
from collections import deque


class SensorLink:
    def __init__(self, delay_s, loss_rate, seed=20261121):
        self.delay_s = delay_s
        self.loss_rate = loss_rate
        self.random = random.Random(seed)
        self.pending = deque()
        self.dropped = 0

    def step(self, now, values):
        due = None
        while self.pending and self.pending[0][0] <= now:
            due = self.pending.popleft()[1]
        if self.random.random() < self.loss_rate:
            self.dropped += 1
        else:
            self.pending.append((now + self.delay_s, values))
        return due
