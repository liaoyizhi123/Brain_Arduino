# Python
#

import serial

MAX_PACKET_LENGTH = 32
EEG_POWER_BANDS = 8

class Brain:
    def __init__(self, brain_stream):

        self.brain_stream = brain_stream
        self.fresh_packet = False
        self.in_packet = False
        self.packet_index = 0
        self.packet_length = 0
        self.eeg_power_length = 0
        self.has_power = False
        self.checksum = 0
        self.checksum_accumulator = 0

        self.signal_quality = 200
        self.attention = 0
        self.meditation = 0

        self.last_byte = None
        self.latest_error = ""
        self.csv_buffer = ""
        self.packet_data = [0] * MAX_PACKET_LENGTH
        self.eeg_power = [0] * EEG_POWER_BANDS

        self.clear_eeg_power()

    def update(self):
        if self.brain_stream.in_waiting:
            latest_byte = ord(self.brain_stream.read(1))  # read a byte of data

            # Build a packet if we know we're and not just listening for sync bytes.
            if self.in_packet:
                # First byte after the sync bytes is the length of the upcoming packet.
                # 获取数据包长度
                if self.packet_index == 0:
                    self.packet_length = latest_byte
                    # Catch error if packet is too long
                    if self.packet_length > MAX_PACKET_LENGTH:
                        # Packet exceeded max length
                        # Send an error
                        self.latest_error = f"Error: Packet length {self.packet_length} is too long."
                        self.in_packet = False

                elif self.packet_index <= self.packet_length:
                    # Run of the mill data bytes.

                    # Print them here

                    # Store the byte in an array for parsing later.
                    # 读取到字节存入数据包数组
                    self.packet_data[self.packet_index - 1] = latest_byte

                    # Keep building the checksum.
                    self.checksum_accumulator += latest_byte

                elif self.packet_index > self.packet_length:
                    # 数据包末尾是checksum byte.
                    # We're at the end of the data payload.

                    # Check the checksum.
                    self.checksum = latest_byte
                    self.checksum_accumulator = 255 - self.checksum_accumulator

                    # Do they match?
                    if self.checksum == self.checksum_accumulator:
                        if self.parse_packet():  # Parse the packet
                            self.fresh_packet = True
                        else:
                            # Parsing failed, send an error.
                            self.latest_error = "Error: Could not parse packet."
                            # good place to print the packet if debugging
                    else:
                        # Checksum mismatch, send an error.
                        self.latest_error = "Error: Checksum mismatch."
                        # good place to print the packet if debugging

                    # End of packet
                    # Reset, prep for next packet
                    self.in_packet = False

                self.packet_index += 1

            # 1. looking for the start of the packet
            if latest_byte == 170 and self.last_byte == 170 and not self.in_packet:
                # 170 (0xAA) 是数据包的同步字节。检测到连续的两个 170 字节表示数据包的开始，进入数据包读取状态
                # Start of packet
                self.in_packet = True
                self.packet_index = 0
                self.checksum_accumulator = 0

            # keep track of the last byte so we can find the sync byte pairs.
            self.last_byte = latest_byte

        if self.fresh_packet:
            self.fresh_packet = False
            return True
        else:
            return False

    def clear_packet(self):
        self.packet_data = [0] * MAX_PACKET_LENGTH

    def clear_eeg_power(self):
        # Zero the power bands.
        self.eeg_power = [0] * EEG_POWER_BANDS

    def parse_packet(self):
        # Parse the packet
        # Loop through the packet, extracting data.
        # Based on mindset_communications_protocol.pdf from the Neurosky Mindset SDK.
        # Returns true if passing succeeds
        self.has_power = False
        parse_success = True

        # clear the eeg power to make sure we're honest about missing values
        self.clear_eeg_power()

        i=0
        # iterate over each byte in the data packet
        while i < self.packet_length:
            if self.packet_data[i] == 0x02:  # signal quality
                self.signal_quality = self.packet_data[i + 1]
                i += 1
            elif self.packet_data[i] == 0x04:  # attention
                self.attention = self.packet_data[i + 1]
                i += 1
            elif self.packet_data[i] == 0x05:  # meditation
                self.meditation = self.packet_data[i + 1]
                i += 1
            elif self.packet_data[i] == 0x83:  # ASIC_EEG_POWER
                # ASIC_EEG_POWER: eight big-endian 3-uint8_t unsigned integer values representing
                # delta, theta, low-alpha high-alpha, low-beta, high-beta, low-gamma, and mid-gamma EEG band power values

                # The next uint8_t sets the length, usually 24 (Eight 24-bit numbers... big endian?)
                # We do not use this value so let's skip it and just increment i
                i += 1
                for j in range(EEG_POWER_BANDS):
                    self.eeg_power[j] = (self.packet_data[i + 1] << 16) | (self.packet_data[i + 2] << 8) | self.packet_data[i + 3]
                    i += 3
                self.has_power = True
                # This seems to happen once during start-up on the force trainer. Strange. Wise to wait a couple of packets before
                # you start reading.
            elif self.packet_data[i] == 0x80:
                # We dont' use this value so let's skip it and just increment i
                # 跳过原始脑波数据
                i += 2
            else:
                # Unknown code
                parse_success = False
                break
            i += 1

        return parse_success

    def read_errors(self):
        return self.latest_error

    # Keeping this around for debug use
    def read_csv(self):
        if self.has_power:
            self.csv_buffer = f"{self.signal_quality},{self.attention},{self.meditation}," + ",".join(map(str, self.eeg_power))
        else:
            self.csv_buffer = f"{self.signal_quality},{self.attention},{self.meditation}"
        return self.csv_buffer

        # if self.has_power:
        #     self.csv_buffer = ",".join(map(str, self.eeg_power))
        # return self.csv_buffer

    def read_signal_quality(self):
        return self.signal_quality

    def read_attention(self):
        return self.attention

    def read_meditation(self):
        return self.meditation

    def read_eeg_power(self):
        return self.eeg_power

    def read_delta(self):
        return self.eeg_power[0]

    def read_theta(self):
        return self.eeg_power[1]

    def read_low_alpha(self):
        return self.eeg_power[2]

    def read_high_alpha(self):
        return self.eeg_power[3]

    def read_low_beta(self):
        return self.eeg_power[4]

    def read_high_beta(self):
        return self.eeg_power[5]

    def read_low_gamma(self):
        return self.eeg_power[6]

    def read_mid_gamma(self):
        return self.eeg_power[7]


if __name__ == '__main__':
    port_num = 9600
    brain_stream = serial.Serial('COM3', port_num)  # 串口端口
    brain = Brain(brain_stream)

    while True:
        if brain.update():
            print("New data packet received:")
            print(brain.read_csv())
        else:
            print("No new data packet.")
