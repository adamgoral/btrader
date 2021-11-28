from tick.hawkes import SimuHawkesSumExpKernels, SimuHawkesMulti, HawkesSumExpKern, HawkesExpKern
import numpy as np

def return_input(input):
    return input

def get_intensity_ratio(decay, buys, sells):
    deduped = []
    for x in buys:
        if x in sells:
            x = x + 0.0001
        deduped.append(x)

    buys = np.array(deduped)
    sells = np.array(sells)

    decays = decay * np.ones((2, 2))
    learner = HawkesExpKern(decays, penalty='elasticnet',)
    inputs = [buys, sells]
    learner.fit(inputs)
    est_intensities = learner.estimated_intensity(inputs, 1)
    buy_intensity = est_intensities[0][0]
    sell_intensity = est_intensities[0][1]
    time_series = est_intensities[1]
    intensity_ratio = buy_intensity/sell_intensity
    return intensity_ratio[-1]