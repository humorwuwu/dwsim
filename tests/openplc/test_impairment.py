#!/usr/bin/env python3
"""Deterministic checks for the impaired Modbus sensor link."""

import unittest

from impairment import SensorLink


class SensorLinkTests(unittest.TestCase):
    def test_delays_sensor_frame_until_due(self):
        link = SensorLink(0.02, 0.0)
        self.assertIsNone(link.step(0.0, (1, 2, 3)))
        self.assertIsNone(link.step(0.019, (4, 5, 6)))
        self.assertEqual(link.step(0.020, (7, 8, 9)), (1, 2, 3))

    def test_total_loss_never_delivers_a_frame(self):
        link = SensorLink(0.02, 1.0)
        self.assertIsNone(link.step(0.0, (1,)))
        self.assertIsNone(link.step(0.02, (2,)))
        self.assertEqual(link.dropped, 2)

    def test_partial_loss_drops_about_configured_fraction(self):
        link = SensorLink(0.02, 0.1, seed=7)
        for index in range(1000):
            link.step(index * 0.1, (index,))
        self.assertGreater(link.dropped, 50)
        self.assertLess(link.dropped, 150)


if __name__ == "__main__":
    unittest.main()
