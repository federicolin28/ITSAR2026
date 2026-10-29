using UnityEngine;


public class LifeCycleLogger : MonoBehaviour
{
    //Awake è eseguitos solo alla prima attivazione in scena dell'oggetto
    void Awake()
    {
        Debug.Log($"Eseguo Awake di {gameObject.name}");
    }


    //OnEnable è eseguito a ogni rittivazione dell'oggetto
    void OnEnable()
    {
        Debug.Log($"Eseguo OnEnable di {gameObject.name}");
    }


    //Start è eseguito solo al primo avvio della scena
    void Start()
    {
        Debug.Log($"Eseguo Start di {gameObject.name}");
    }


    //Update è chiamato a ogni frame
    void Update()
    {
        Debug.Log($"Eseguo Update di {gameObject.name}");
    }


    //FixedUpdate è chiamato a intervalli fissi mentre l'ogetto è attivo (calcoli fisici)
    private void FixedUpdate()
    {
        Debug.Log($"Eseguo FixedUpdate di {gameObject.name}");
    }


    //LateUpdate è chiamato al termine del ciclo Update/FixedUpdate come ultimo step del ciclo
    private void LateUpdate()
    {
        Debug.Log($"Eseguo LateUpdate di {gameObject.name}");
    }


    //OnDisable è chiamato alla disattivazione dell'oggetto
    private void OnDisable()
    {
        Debug.Log($"Eseguo OnDisable di {gameObject.name}");
    }


    //OnDestroy è chiamato alla distruzione dell'oggetto
    private void OnDestroy()
    {
        Debug.Log($"Eseguo OnDestroy di {gameObject.name}");
    }
}